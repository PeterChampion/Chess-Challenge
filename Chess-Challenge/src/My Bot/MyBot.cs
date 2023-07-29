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
    private int turnCount, totalPieceValue, opponentTotalPieceValue;
    private List<Piece> movedPiecesList = new();

    public Move Think(Board board, Timer timer)
    {
        turnCount += 1;
        totalPieceValue = GetTotalPieceValues(board, out opponentTotalPieceValue);
        var bestMoveEvaluation = int.MinValue;
        var bestPotentialMoves = new List<Move>();

        //System.Threading.Thread.Sleep(1500);

        foreach (var move in board.GetLegalMoves())
        {
            var moveScore = EvaluateMove(board, move);
            if (moveScore > bestMoveEvaluation)
            {
                bestPotentialMoves.Clear();
                bestPotentialMoves.Add(move);
                bestMoveEvaluation = moveScore;
            }
            else if (moveScore == bestMoveEvaluation)
                bestPotentialMoves.Add(move);            
        }
        var chosenMove = bestPotentialMoves[new Random().Next(0, bestPotentialMoves.Count)];

        var movePiece = board.GetPiece(chosenMove.StartSquare);
        if (!movedPiecesList.Contains(movePiece))
            movedPiecesList.Add(movePiece);

        return chosenMove;
    }

    private int GetTotalPieceValues(Board board, out int totalOpponentPieceValue)
    {
        var ourTotalPieceValue = 0;
        totalOpponentPieceValue = 0;
        for (int i = 0; i < 64; i++)
        {
            var piece = board.GetPiece(new Square(i));
            var pieceValue = pieceValues[(int)piece.PieceType];
            totalPieceValue = board.IsWhiteToMove == piece.IsWhite ? ourTotalPieceValue += pieceValue : totalOpponentPieceValue += pieceValue;
        }

        return ourTotalPieceValue;
    }

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
            if (turnCount <= 2) //&& (move.TargetSquare.Name == "e4" || move.TargetSquare.Name == "d3" || move.TargetSquare.Name == "e5" || move.TargetSquare.Name == "d6"))
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
        /// Encourage moving undeveloped Knights/Bishops after the initial pawn moves
        else if (turnCount > 2 && turnCount <= 6 && (move.MovePieceType == PieceType.Bishop || move.MovePieceType == PieceType.Knight))
            evalScore += 25;

        /// Encourage captures by the value of the captured piece.
        if (IsMoveCapture(board, move, out var capturedPieceValue))
            evalScore += capturedPieceValue;

        /// Discourage moves that will likely result in our capture by the value of our piece.
        if (DoesMovePutPieceAtRisk(board, move))
            evalScore -= pieceValues[(int)move.MovePieceType];

        /// Encourage checks slightly after initial development.
        if (turnCount >= 6  && IsMoveCheck(board, move))
            evalScore += 25;

        /// Discourage moves that prevent us from castling massively.
        if ((board.HasKingsideCastleRight(board.IsWhiteToMove) || board.HasQueensideCastleRight(board.IsWhiteToMove)) && !move.IsCastles && move.MovePieceType == PieceType.King)
            evalScore -= 1000;

        /// Encourage moving a piece that has not been moved before slightly.
        if (!movedPiecesList.Contains(board.GetPiece(move.StartSquare)))
            evalScore += 15;

        /// Encourage promotions massively.
        if (!board.SquareIsAttackedByOpponent(move.TargetSquare) && move.PromotionPieceType == PieceType.Queen)
            evalScore += 900;

        /// Encourage moving away if we are attacked.
        if (board.SquareIsAttackedByOpponent(move.StartSquare)) 
            evalScore += 150;

        /// Discourage draws if winning in overall piece value, encourage if we are losing however (Can't win 'em all!) ;)
        if (DoesMoveResultInDraw(board, move))
            evalScore = totalPieceValue - opponentTotalPieceValue <= -1000 ? evalScore -= 9999 : evalScore += 9999;

        /// King moves that aren't castling.
        if (move.MovePieceType == PieceType.King && !move.IsCastles)
        {
            /// Encourage moves towards the opposing King if we don't have much piece value left on the board (Inferring we are in the endgame.)
            if (totalPieceValue <= 1000)
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
                evalScore = totalPieceValue - opponentTotalPieceValue <= -1000 ? evalScore -= 9999 : evalScore += 9999;

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
    /// Return whether the move captures an opposing piece, outing the value of the piece captured and the value of the attacker.
    /// </summary>
    /// <param name="board"></param>
    /// <param name="move"></param>
    /// <param name="capturedPieceValue"></param>
    /// <returns></returns>
    private bool IsMoveCapture(Board board, Move move, out int capturedPieceValue)
    {
        capturedPieceValue = pieceValues[(int)board.GetPiece(move.TargetSquare).PieceType];
        return !IsSquareUnderThreat(board, move.TargetSquare);

        //var capturedPiece = board.GetPiece(move.TargetSquare);
        //capturedPieceValue = pieceValues[(int)capturedPiece.PieceType];
        //attackingPieceValue = pieceValues[(int)move.MovePieceType];
        //return capturedPieceValue > 0;
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
    /// Returns whether
    /// </summary>
    /// <param name="board"></param>
    /// <param name="move"></param>
    /// <param name="pieceValue"></param>
    /// <returns></returns>
    private bool DoesMovePutPieceAtRisk(Board board, Move move)
    {
        // TODO: Check if the piece is actually at risk - Are we defended and the attackers piece value is greater than ours.
        //pieceValue = pieceValues[(int)move.MovePieceType];
        //return board.SquareIsAttackedByOpponent(move.TargetSquare);
        board.MakeMove(move);
        board.ForceSkipTurn();
        var underThreat = IsSquareUnderThreat(board, move.TargetSquare);
        board.UndoSkipTurn();
        board.UndoMove(move);
        return underThreat;
    }

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

    private int GetKingsDistance(Board board)
    {
        var ourKingSquare = board.GetKingSquare(board.IsWhiteToMove);
        var opponentKingSquare = board.GetKingSquare(!board.IsWhiteToMove);
        return Math.Abs(ourKingSquare.File - opponentKingSquare.File) + Math.Abs(ourKingSquare.Rank - opponentKingSquare.Rank);
    }

    private bool IsMoveRepeat(Board board, Move move)
    {
        board.MakeMove(move);
        var isRepeat = board.IsRepeatedPosition();
        board.UndoMove(move);
        return isRepeat;
    }
}