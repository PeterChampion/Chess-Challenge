using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using ChessChallenge.API;

public class EvilerBot : IChessBot
{
    /// <summary>
    /// Piece values: null, pawn, knight, bishop, rook, queen, king
    /// </summary>
    private int[] pieceValues = { 0, 100, 300, 300, 500, 900, 0 };

    private int turnCount, ourTotalPieceValue, opponentTotalPieceValue;

    private HashSet<Piece> movedPieces = new();
    private Board board;
    private int turnsSinceLastPawnMove;

    public Move Think(Board board, Timer timer)
    {
        this.board = board;
        turnCount += 1;
        var bestMoveEvaluation = int.MinValue;
        Move bestMove = new();
        //var bestPotentialMoves = new List<Move>();

        /// Cache our and our opponents total piece values on the board.
        ourTotalPieceValue = GetTotalPieceValues(out opponentTotalPieceValue);

        /// Force the bot to delay its move for testing purposes in bot vs bot matches.
        //System.Threading.Thread.Sleep(500);

        /// Iterate over all possible moves.
        foreach (var move in board.GetLegalMoves())
        {
            /// Evaluate the current move.
            var moveScore = EvaluateMove(board, move);

            if (moveScore > bestMoveEvaluation)
            {
                bestMoveEvaluation = moveScore;
                bestMove = move;
            }
        }
        turnsSinceLastPawnMove = bestMove.MovePieceType == PieceType.Pawn ? 0 : turnsSinceLastPawnMove++;

        /// Cache the piece that's moving and if its not been moved before, add it to the 'movedPieces' collection.
        var movePiece = board.GetPiece(bestMove.StartSquare);
        movedPieces.Add(movePiece);

        return bestMove;
    }

    /// <summary>
    /// Return the current total value of all our pieces and our opponents.
    /// </summary>
    /// <param name="totalOpponentPieceValue"></param>
    /// <returns></returns>
    private int GetTotalPieceValues(out int totalOpponentPieceValue)
    {
        var ourTotalPieceValue = 0;
        totalOpponentPieceValue = 0;

        /// Iterate over every square on the board.
        var i = 64;
        while (i-- > 0)
        {
            /// Cache the piece and depending on whether it matches our colour add it to our total piece value score, or our opponents.
            var piece = board.GetPiece(new Square(i));
            var pieceValue = pieceValues[(int)piece.PieceType];
            if (board.IsWhiteToMove == piece.IsWhite)
                ourTotalPieceValue += pieceValue;
            else
                totalOpponentPieceValue += pieceValue;
        }

        return ourTotalPieceValue;
    }

    /// <summary>
    /// Evaluates the given move and assigns it an 'evalScore' based on various parameters such as whether the move is checkmate, en passant, check, capture, castles, moves to a dangerous/risky square, etc.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="move"></param>
    /// <returns></returns>
    private int EvaluateMove(Board board, Move move)
    {
        var evalScore = 0;
        var isWhite = board.IsWhiteToMove;
        var pieceValue = pieceValues[(int)move.MovePieceType];
        var opponentKingSquare = board.GetKingSquare(isWhite);
        var kingDistanceFromPiece = GetKingsDistanceFromSquare(move.StartSquare);
        var kingDistancePreMove = GetKingsDistanceFromSquare(opponentKingSquare);
        var targetSquare = move.TargetSquare;
        var startSquare = move.StartSquare;
        var movePieceType = move.MovePieceType;

        /// Encourage captures by the value of the captured piece minus the value of the attacking pieces / 10 to encourage taking with lower value pieces first in exchanges.
        if (IsMoveCapture(move, out var capturedPieceValue))
            evalScore += capturedPieceValue - (pieceValue / 10);

        /// Discourage moves (with the King) that prevent us from castling massively.
        if ((board.HasKingsideCastleRight(isWhite) || board.HasQueensideCastleRight(isWhite)) && !move.IsCastles && movePieceType == PieceType.King)
            evalScore -= 1000;

        /// Encourage moves to a safe position if our current one is under threat (Not defended sufficiently).
        if (IsSquareUnderThreat(startSquare) && board.SquareIsAttackedByOpponent(startSquare))
            evalScore += 150;

        board.MakeMove(move);

        var kingDistancePostMove = GetKingsDistanceFromSquare(opponentKingSquare);

        /// Discourage if the move is a Capture and we are taking with a Pawn but would be taking an undefended piece and creating stacked pawns.
        if (move.IsCapture && movePieceType == PieceType.Pawn)
            if (MoveCreatesStackedPawn() && !IsSquareUnderThreat(targetSquare))
                evalScore -= capturedPieceValue / 2;

        /// Always play mate in 1 or En Passant (Which is a moral checkmate).
        if (board.IsInCheckmate() || move.IsEnPassant)
            evalScore = 9999999;

        /// Encourage checks after initial development.
        if (turnCount >= 8 && board.IsInCheck())
            evalScore += 45;

        /// Discourage repeated positions that aren't checks.
        if (board.IsRepeatedPosition() && !board.IsInCheck())
            evalScore -= 75;

        /// Encourage castling.
        if (move.IsCastles)
            evalScore += 125;

        /// Pawn Moves
        if (movePieceType == PieceType.Pawn)
        {
            /// Encourage center pawns to move forward for the first two moves.
            if (turnCount <= 2 && (startSquare.Rank == 1 || startSquare.Rank == 6))
            {
                switch (targetSquare.Name)
                {
                    case "e4":
                    case "d3":
                    case "e5":
                    case "d6":
                        evalScore += 75;
                        break;
                }
            }
            /// Encourage later game pawn moves.
            else if (turnCount >= 20)
                evalScore += 15;

            /// Discourage moving pawns in front of the king.
            if (kingDistanceFromPiece <= 3)
                evalScore -= 60;

            /// Encourage a pawn move if one hasn't been made for a while.
            if (turnsSinceLastPawnMove >= 20)
                evalScore += 75;
        }
        /// Opening development moves.
        else if (turnCount > 2 && turnCount <= 10)
        {
            /// Knight/Bishop moves.
            if (pieceValue == 300)
            {
                /// Encourage moving these pieces in opening development.
                evalScore += 10;

                /// Further encourage moving these pieces if they are on their starting file.
                if ((isWhite && (startSquare.File == 5 || startSquare.File == 6)) || (!isWhite && (startSquare.File == 5 || startSquare.File == 6)))
                    evalScore += 50;

            }
            /// Discourage moving the Queen in the early game.
            else if (pieceValue == 900)
                evalScore -= 50;
        }

        /// Discourage pieces from moving to the backrank.
        if ((!isWhite && targetSquare.Rank == 0) || (isWhite && targetSquare.Rank == 7) ||
            (pieceValue == 300 && (targetSquare.File == 0 || targetSquare.File == 7)))
            evalScore -= 50;

        /// Encourage moving a piece that has not been moved before.
        if (!movedPieces.Contains(board.GetPiece(targetSquare)))
            evalScore += 15;

        /// Encourage safe promotions.
        if (!board.SquareIsAttackedByOpponent(targetSquare) && move.IsPromotion && move.PromotionPieceType == PieceType.Queen)
            evalScore += 900;

        /// Discourage draws if winning in overall piece value, encourage if we are losing by a significant margin however (Can't win 'em all!)
        if (DoesMoveResultInDraw(move))
            evalScore += ourTotalPieceValue - opponentTotalPieceValue <= -1200 ? -9999 : 9999;

        /// King moves that aren't castling.
        if (movePieceType == PieceType.King && !move.IsCastles)
        {
            /// Encourage moves towards the opposing King if we don't have much piece value left on the board (Inferring we are in the endgame.)
            if (ourTotalPieceValue <= 1000)
            {
                if (kingDistancePostMove < kingDistancePreMove)
                    evalScore += 50;
            }
            /// Otherwise if we aren't in the endgame, discourage moving the King towards the opponent.
            else
                evalScore -= 50;
        }

        /// Assess our opponents immediate next move which could follow this one.
        foreach (var opponentMove in board.GetLegalMoves())
        {
            /// Don't open ourselves up to captures.
            if (IsMoveCapture(opponentMove, out var ourCapturedPieceValue))
                evalScore -= ourCapturedPieceValue;

            board.MakeMove(opponentMove);

            /// Don't open ourselves to 1 move check mates.
            if (board.IsInCheckmate())
                evalScore -= 9999;

            /// Don't open ourselves up to checks or EnPassant.
            if (board.IsInCheck() || opponentMove.IsEnPassant)
                evalScore -= 100;

            /// Evaluate possible draw positions and if we want those.
            if (DoesMoveResultInDraw(opponentMove))
                evalScore += ourTotalPieceValue - opponentTotalPieceValue <= -1200 ? -9999 : 9999;

            board.UndoMove(opponentMove);
        }
        board.UndoMove(move);

        return evalScore;
    }

    /// <summary>
    /// Returns whether the move safely captures an opposing piece, outing the value of the piece captured.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="move"></param>
    /// <param name="capturedPieceValue"></param>
    /// <returns></returns>
    private bool IsMoveCapture(Move move, out int capturedPieceValue)
    {
        capturedPieceValue = pieceValues[(int)board.GetPiece(move.TargetSquare).PieceType];
        return !IsSquareUnderThreat(move.TargetSquare) && capturedPieceValue > 0;
    }

    /// <summary>
    /// Returns whether the move will result in a draw.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="move"></param>
    /// <returns></returns>
    private bool DoesMoveResultInDraw(Move move) => board.IsInsufficientMaterial() || (!board.IsInCheck() && board.GetLegalMoves().Length == 0) || board.FiftyMoveCounter >= 100;

    /// <summary>
    /// Returns whether a square is under threat by comparing the total value of defenders vs attackers.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="square"></param>
    /// <returns></returns>
    private bool IsSquareUnderThreat(Square square)
    {
        int defenderScore = 0, attackerScore = 0;

        foreach (var move in board.GetLegalMoves())
            if (move.TargetSquare == square)
                defenderScore += pieceValues[(int)move.MovePieceType];

        board.ForceSkipTurn();
        foreach (var opponentMove in board.GetLegalMoves())
            if (opponentMove.TargetSquare == square)
                attackerScore += pieceValues[(int)opponentMove.MovePieceType];
        board.UndoSkipTurn();

        return defenderScore < attackerScore;
    }

    /// <summary>
    /// Returns the distance between both kings.
    /// </summary>
    /// <param name="board"></param>
    /// <returns></returns>
    private int GetKingsDistanceFromSquare(Square square)
    {
        var ourKingSquare = board.GetKingSquare(!board.IsWhiteToMove);
        return Math.Abs(ourKingSquare.File - square.File) + Math.Abs(ourKingSquare.Rank - square.Rank);
    }

    /// <summary>
    /// Does the move create a stacked pawn structure? (2 Pawns in the same File).
    /// </summary>
    /// <returns></returns>
    private bool MoveCreatesStackedPawn()
    {
        var pawnPieceList = board.GetPieceList(PieceType.Pawn, !board.IsWhiteToMove).OrderBy(piece => piece.Square.File);
        var previousFile = -1;

        foreach (var piece in pawnPieceList)
        {
            if (piece.Square.File == previousFile)
                return true;

            previousFile = piece.Square.File;
        }

        return false;
    }
}