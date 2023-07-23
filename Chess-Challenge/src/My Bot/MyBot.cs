using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using ChessChallenge.API;

public struct EvaluatedMove
{
    public Move Move { get; set; }
    public float Evaluation { get; set; }
}

public class MyBot : IChessBot
{
    /*
     * Checks
     * Captures
     * Attacks
     */

    // TODO:
    // - Evaluate the position AFTER our move.
    // - Check for our captures - Done
    // - Check for opponent captures - Done
    // - Evaluate best capture - Done?
    // - Evaluate if capture puts piece at risk - Done
    // - Evaluate if we should capture anyway - Done
    // - Check for promotions - Done
    // - Check for SAFE promotions - Done
    // - Evaluate Captures vs Promotions - Done
    // - Do we defend a previously undefended piece?
    // - Do we leave a piece hanging?
    // - Evaluation formula
    // - Look at opponents potential captures


    /*
     * Iterate over all legal moves.
     * Check each move, assign each move an evaluation based on its impact to the board.
     */

    //public Move Think(Board board, Timer timer)
    //{
    //    return EvaluateMoves(board.GetLegalMoves(), 5).OrderByDescending(m => m).First().Move;
    //}

    //private EvaluatedMove[] EvaluateMoves(Move[] moves, int depth)
    //{
    //    var evaluatedMoves = new EvaluatedMove[moves.Length];
    //    for (int i = 0; i < depth; i++)
    //    {
    //        foreach (var move in moves)
    //        {

    //        }
    //    }

    //    return evaluatedMoves;
    //}

    // Piece values: null, pawn, knight, bishop, rook, queen, king
    private int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };

    public Move Think(Board board, Timer timer)
    {
        /// Cache all legal moves.
        var allLegalMoves = board.GetLegalMoves();
        var notTerribleMoves = new List<Move>();

        /// Default to picking a random move to play.
        var moveToPlay = new Move();
        var highestValueCapture = 0;
        //var highestValueAttack = 0;
        //var moveReason = string.Empty;

        //System.Threading.Thread.Sleep(500);

        foreach (Move move in allLegalMoves)
        {
            /// If the move is Checkmate, play it.
            if (IsMoveCheckmate(board, move))
            {
                moveToPlay = move;
                break;
            }

            /// Skip if move results in a draw.
            if (DoesMoveResultInDraw(board, move))
                continue;

            var movePutsAtRisk = MovePutsPieceAtRisk(board, move, out var pieceAtRiskValue);
            var moveCaptures = IsMoveCapture(board, move, out var capturedPieceValue);
            var moveSafelyPromotes = IsMoveSafePromotion(board, move);
            var moveDisqualifysCastling = DoesMoveDisqualifyCastling(board, move);
            var moveChecks = IsMoveCheck(board, move);
            //var moveLeavesPieceHanging = PiecesUnderAttacks(board, move);

            /// Evaluate if the move is a capture.
            if (capturedPieceValue > highestValueCapture)
            {
                /// Evaluate if the capture puts us at risk - Don't capture if we would immediately be taken and we are worth more or the same.
                if ((movePutsAtRisk && pieceAtRiskValue >= capturedPieceValue) || move.MovePieceType == PieceType.King && (board.HasKingsideCastleRight(board.IsWhiteToMove) || board.HasQueensideCastleRight(board.IsWhiteToMove)))
                    continue;

                moveToPlay = move;
                //moveReason = $"We can capture a {board.GetPiece(move.TargetSquare)} with a {move.MovePieceType}.";
                highestValueCapture = capturedPieceValue;
            }

            /// If we can promote (to a Queen) safely and if we can't currently capture a piece, use this Move.
            if (move.IsPromotion && move.PromotionPieceType == PieceType.Queen && moveSafelyPromotes && highestValueCapture < pieceValues[(int)PieceType.Knight])
            {
                moveToPlay = move;
                //moveReason = $"We can promote to a {move.PromotionPieceType} safely as a {move.MovePieceType}.";
            }

            /// If the move puts our opponent into check and the best possible capture we have so far is less than a Rook, use this Move.
            if (!movePutsAtRisk && moveChecks && highestValueCapture < pieceValues[(int)PieceType.Rook])
            {
                moveToPlay = move;
                //moveReason = $"We can give a check.";
            }

            /// If the move Castles and does not put us at risk, and we can't currently capture a piece or promote safely or cause a check, use this Move.
            if (move.IsCastles && !movePutsAtRisk && highestValueCapture < pieceValues[(int)PieceType.Knight] && !moveSafelyPromotes && !moveChecks)
            {
                moveToPlay = move;
                //moveReason = $"We can castle.";
            }

            if (!movePutsAtRisk && !moveDisqualifysCastling)
                notTerribleMoves.Add(move);
        }

        /// If we have not found a move, make a random move from our 'Not Terrible' moves if possible, otherwise just play a random move.
        if (moveToPlay.IsNull)
        {
            moveToPlay = notTerribleMoves.Count > 0 ? notTerribleMoves[new Random().Next(notTerribleMoves.Count)] : allLegalMoves[new Random().Next(allLegalMoves.Length)];
            //moveReason = notTerribleMoves.Contains(moveToPlay) ? "We chose from our not terrible moves" : "We're bad and chose randomly.";
        }

        //Console.WriteLine(moveReason);
        return moveToPlay;
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

    private bool IsMoveCapture(Board board, Move move, out int capturedPieceValue)
    {
        var capturedPiece = board.GetPiece(move.TargetSquare);
        capturedPieceValue = pieceValues[(int)capturedPiece.PieceType];
        return capturedPieceValue > 0;
    }

    private bool DoesMoveResultInDraw(Board board, Move move)
    {
        board.MakeMove(move);
        var isDraw = board.IsDraw();
        board.UndoMove(move);
        return isDraw;
    }

    private bool IsMoveCheck(Board board, Move move)
    {
        board.MakeMove(move);
        var isCheck = board.IsInCheck();
        board.UndoMove(move);
        return isCheck;
    }

    private bool IsMoveSafePromotion(Board board, Move move)
    {
        return !board.SquareIsAttackedByOpponent(move.TargetSquare);
    }

    private bool MovePutsPieceAtRisk(Board board, Move move, out int pieceValue)
    {
        pieceValue = pieceValues[(int)move.MovePieceType];
        var atRisk = board.SquareIsAttackedByOpponent(move.TargetSquare);
        return atRisk;
    }

    private bool DoesMoveDisqualifyCastling(Board board, Move move)
    {
        return !move.IsCastles && (move.MovePieceType == PieceType.King || move.MovePieceType == PieceType.Rook);
    }
}