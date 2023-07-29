using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using ChessChallenge.API;

public class MyBot : IChessBot
{
    /// <summary>
    /// Piece values: null, pawn, knight, bishop, rook, queen, king
    /// </summary>
    private int[] pieceValues = { 0, 100, 300, 300, 500, 900, 0 };

    private int turnCount, ourTotalPieceValue, opponentTotalPieceValue;

    private List<Piece> movedPieces = new();

    public Move Think(Board board, Timer timer)
    {
        turnCount += 1;
        var bestMoveEvaluation = int.MinValue;
        var bestPotentialMoves = new List<Move>();

        /// Cache our and our opponents total piece values on the board.
        ourTotalPieceValue = GetTotalPieceValues(board, out opponentTotalPieceValue);

        /// Force the bot to delay its move for testing purposes in bot vs bot matches.
        //System.Threading.Thread.Sleep(1500);

        /// Iterate over all possible moves.
        foreach (var move in board.GetLegalMoves())
        {
            /// Evaluate the current move.
            var moveScore = EvaluateMove(board, move);

            /// If the evaluated move is the best so far, clear the list of potential best moves and add this as the only entry, record the score.
            if (moveScore > bestMoveEvaluation)
            {
                bestPotentialMoves.Clear();
                bestPotentialMoves.Add(move);
                bestMoveEvaluation = moveScore;
            }
            /// If the evaluated move has the same score as our current best, add it to the collection.
            else if (moveScore == bestMoveEvaluation)
                bestPotentialMoves.Add(move);            
        }

        /// Choose a move from our best potential moves, these are all 'equal' in their quality so for now select randomly.
        var chosenMove = bestPotentialMoves[new Random().Next(0, bestPotentialMoves.Count)];

        /// Cache the piece that's moving and if its not been moved before, add it to the 'movedPieces' collection.
        var movePiece = board.GetPiece(chosenMove.StartSquare);
        if (!movedPieces.Contains(movePiece))
            movedPieces.Add(movePiece);

        return chosenMove;
    }

    /// <summary>
    /// Return the current total value of all our pieces and our opponents.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="totalOpponentPieceValue"></param>
    /// <returns></returns>
    private int GetTotalPieceValues(Board board, out int totalOpponentPieceValue)
    {
        var ourTotalPieceValue = 0;
        totalOpponentPieceValue = 0;

        /// Iterate over every square on the board.
        for (int i = 0; i < 64; i++)
        {
            /// Cache the piece and depending on whether it matches our colour add it to our total piece value score, or our opponents.
            var piece = board.GetPiece(new Square(i));
            var pieceValue = pieceValues[(int)piece.PieceType];
            this.ourTotalPieceValue = board.IsWhiteToMove == piece.IsWhite ? ourTotalPieceValue += pieceValue : totalOpponentPieceValue += pieceValue;
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

        /// Always play mate in 1 or En Passant (Which is a moral checkmate).
        if (IsMoveCheckmate(board, move) || move.IsEnPassant)
            return int.MaxValue;

        /// Encourage castling significantly.
        if (move.IsCastles)
            evalScore += 75;

        /// Discourage repeated positions significantly.
        if (IsMoveRepeat(board, move))
            evalScore -= 75;

        /// Encourage opening pawn moves and later pawn moves slightly.
        if (move.MovePieceType == PieceType.Pawn)
        {
            /// Encourage center pawns to move forward for the first two moves.
            if (turnCount <= 2)
            {
                switch (move.TargetSquare.Name)
                {
                    case "e4":
                    case "d3":
                    case "e5":
                    case "d6":
                        evalScore += 15;
                        break;
                }
            }
            else if (turnCount >= 20)
                evalScore += 15;
        }
        else if (turnCount > 2 && turnCount <= 10)
        {
            /// Encourage moving undeveloped Knights/Bishops after the initial pawn moves
            if (move.MovePieceType == PieceType.Bishop || move.MovePieceType == PieceType.Knight)
                evalScore += 25;
            /// Discourage moving the Queen in the early game moderately.
            else if (move.MovePieceType == PieceType.Queen)
                evalScore -= 50;
        }
        /// Discourage pieces from moving to the backrank slightly.
        if (move.MovePieceType != PieceType.Pawn && (board.IsWhiteToMove && move.TargetSquare.Rank == 1 || !board.IsWhiteToMove && move.TargetSquare.Rank == 8))
            evalScore -= 15;

        /// Encourage captures by the value of the captured piece.
        if (IsMoveCapture(board, move, out var capturedPieceValue))
            evalScore += capturedPieceValue;

        /// Encourage checks slightly after initial development.
        if (turnCount >= 8 && IsMoveCheck(board, move))
            evalScore += 25;

        /// Discourage moves (with the King) that prevent us from castling massively.
        if ((board.HasKingsideCastleRight(board.IsWhiteToMove) || board.HasQueensideCastleRight(board.IsWhiteToMove)) && !move.IsCastles && move.MovePieceType == PieceType.King)
            evalScore -= 1000;

        /// Encourage moving a piece that has not been moved before slightly.
        if (!movedPieces.Contains(board.GetPiece(move.StartSquare)))
            evalScore += 15;
        else
            evalScore -= 5;

        /// Encourage safe promotions massively.
        if (!board.SquareIsAttackedByOpponent(move.TargetSquare) && move.PromotionPieceType == PieceType.Queen)
            evalScore += 900;

        /// Encourage moves to a safe position if our current one is under threat (Not defended sufficiently).
        if (IsSquareUnderThreat(board, move.StartSquare) && !DoesMovePutPieceAtRisk(board, move))//(board.SquareIsAttackedByOpponent(move.StartSquare)) 
            evalScore += 150;

        /// Discourage draws if winning in overall piece value, encourage if we are losing by 1000 points however (Can't win 'em all!)
        if (DoesMoveResultInDraw(board, move))
            evalScore = ourTotalPieceValue - opponentTotalPieceValue <= -1200 ? evalScore -= 9999 : evalScore += 9999;

        /// King moves that aren't castling.
        if (move.MovePieceType == PieceType.King && !move.IsCastles)
        {
            /// Encourage moves towards the opposing King if we don't have much piece value left on the board (Inferring we are in the endgame.)
            if (ourTotalPieceValue <= 1000)
            {
                var kingDistancePreMove = GetKingsDistance(board);
                board.MakeMove(move);
                var kingDistancePostMove = GetKingsDistance(board);
                board.UndoMove(move);

                if (kingDistancePostMove < kingDistancePreMove)
                    evalScore += 50;
            }
            /// Otherwise if we aren't in the endgame, discourage moving the King towards the opponent.
            else
                evalScore -= 50;
        }

        /// Assess our opponents immediate next move which could follow this one.
        board.MakeMove(move);
        foreach (var opponentMove in board.GetLegalMoves())
        {
            /// Don't open ourselves to 1 move check mates.
            if (IsMoveCheckmate(board, opponentMove))
                evalScore -= 9999;

            /// Don't open ourselves up to checks.
            if (IsMoveCheck(board, opponentMove))
                evalScore -= 25;

            /// Evaluate possible draw positions and if we want those.
            if (DoesMoveResultInDraw(board, opponentMove))
                evalScore = ourTotalPieceValue - opponentTotalPieceValue <= -1000 ? evalScore -= 9999 : evalScore += 9999;

            /// Don't open ourselves up to captures.
            if (IsMoveCapture(board, opponentMove, out var ourCapturedPieceValue))
                evalScore -= ourCapturedPieceValue;
        }
        board.UndoMove(move);

        return evalScore;
    }

    /// <summary>
    /// Return whether the passed Move is checkmate.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="move"></param>
    /// <returns></returns>
    private bool IsMoveCheckmate(Board board, Move move)
    {
        board.MakeMove(move);
        bool isMate = board.IsInCheckmate();
        board.UndoMove(move);
        return isMate;
    }

    /// <summary>
    /// Return whether the move safely captures an opposing piece, outing the value of the piece captured.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="move"></param>
    /// <param name="capturedPieceValue"></param>
    /// <returns></returns>
    private bool IsMoveCapture(Board board, Move move, out int capturedPieceValue)
    {
        capturedPieceValue = pieceValues[(int)board.GetPiece(move.TargetSquare).PieceType];
        return !IsSquareUnderThreat(board, move.TargetSquare);
    }

    /// <summary>
    /// Returns whether the move will result in a draw.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="move"></param>
    /// <returns></returns>
    private bool DoesMoveResultInDraw(Board board, Move move)
    {
        board.MakeMove(move);
        var isDraw = board.IsInsufficientMaterial() || (!board.IsInCheck() && board.GetLegalMoves().Length == 0) || board.FiftyMoveCounter >= 100;
        board.UndoMove(move);
        return isDraw;
    }

    /// <summary>
    /// Returns whether the Move results in a check.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="move"></param>
    /// <returns></returns>
    private bool IsMoveCheck(Board board, Move move)
    {
        board.MakeMove(move);
        var isCheck = board.IsInCheck();
        board.UndoMove(move);
        return isCheck;
    }

    /// <summary>
    /// Returns whether the move points our piece at risky - Likely to be captured based on the opponents position.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="move"></param>
    /// <param name="pieceValue"></param>
    /// <returns></returns>
    private bool DoesMovePutPieceAtRisk(Board board, Move move)
    {
        board.MakeMove(move);
        board.ForceSkipTurn();
        var underThreat = IsSquareUnderThreat(board, move.TargetSquare);
        board.UndoSkipTurn();
        board.UndoMove(move);
        return underThreat;
    }

    /// <summary>
    /// Returns whether a square is under threat by comparing the total value of defenders vs attackers.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="square"></param>
    /// <returns></returns>
    private bool IsSquareUnderThreat(Board board, Square square)
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
    private int GetKingsDistance(Board board)
    {
        var ourKingSquare = board.GetKingSquare(board.IsWhiteToMove);
        var opponentKingSquare = board.GetKingSquare(!board.IsWhiteToMove);
        return Math.Abs(ourKingSquare.File - opponentKingSquare.File) + Math.Abs(ourKingSquare.Rank - opponentKingSquare.Rank);
    }

    /// <summary>
    /// Returns whether the move results in a repeated position that has already been seen before.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="move"></param>
    /// <returns></returns>
    private bool IsMoveRepeat(Board board, Move move)
    {
        board.MakeMove(move);
        var isRepeat = board.IsRepeatedPosition();
        board.UndoMove(move);
        return isRepeat;
    }    
}