using ChessChallenge.API;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Diagnostics;
using System.Linq;

public class EvilBot : IChessBot
{
    public int[] pieceVals = { 0, 100, 300, 320, 500, 900, 10000 }; // nothing, pawn, knight, bishop, rook, queen, king
    /// <summary>

    /// Blind Bot:
    /// Probably the best chess bot that CANNOT look ahead!
    /// This was a fun challenge!
    /// This bot can only check evaluation for the current move using a very complex hand-made evaluation function
    /// This took a while!
    /// The most major advantage is that it finishes each move in ~5 ms.
    /// In the massive fight, this can only win against bots who check up to like 30 moves.
    /// However I think this would be an intresting experiment and would be fun for the grand finale video.

    /// </summary>
    int movesSinceLastPawnMove = 0;

    int kingDSTfromOpponentKing(Board board)
    {
        Square myKingSquare = board.GetKingSquare(board.IsWhiteToMove);
        Square oKingSquare = board.GetKingSquare(!board.IsWhiteToMove);
        int fileDist = Math.Abs(myKingSquare.File - oKingSquare.File);
        int rankDist = Math.Abs(myKingSquare.Rank - oKingSquare.Rank);
        int dst = fileDist + rankDist;
        return dst;
    }

    public Move[] GetEnemyMoves(Board board)
    {
        board.MakeMove(Move.NullMove);
        Move[] enemyMoves = board.GetLegalMoves();
        board.UndoMove(Move.NullMove);
        return enemyMoves;
    }

    int piecesLeft(Board board)
    {
        int count = 0;
        for (var i = 0; i < 64; i++)
            if (board.GetPiece(new Square(i)).PieceType != PieceType.None) count++;
        return count;
    }

    bool moveIsCheckmate(Board board, Move move)
    {
        board.MakeMove(move);
        bool isMate = board.IsInCheckmate();
        board.UndoMove(move);
        return isMate;
    }

    bool moveIsCheck(Board board, Move move)
    {
        board.MakeMove(move);
        bool isCheck = board.IsInCheck();
        board.UndoMove(move);
        return isCheck;
    }


    bool moveHasBeenPlayed(Board board, Move move)
    {
        board.MakeMove(move);
        bool hasBeenPlayed = board.IsDraw();
        board.UndoMove(move);
        return hasBeenPlayed;
    }


    int evaluateMove(Move move, Board board) // evaluates the move
    {
        int piecesLeftnow = piecesLeft(board);
        PieceType capturedPiece = move.CapturePieceType;
        int eval = 0;
        eval = pieceVals[(int)capturedPiece];
        if (eval > 0) { eval += 5; }
        if (board.SquareIsAttackedByOpponent(move.TargetSquare)) // uh oh here come the piece square tables
        {
            eval -= pieceVals[(int)move.MovePieceType];
        }
        ///<summary>
        /// Piece square tables for all pieces except king.
        /// This also includes that you should push out your queen early game.
        /// This will priortise "good" moves like castling and promoting over worse moves.
        /// It will also transition into endgame tables where your queen and rook are more important.
        /// This is responsible for more than half of the tokens BTW.
        /// </summary>
        if (move.MovePieceType == PieceType.Knight)// Knight piece square table, will prefer to be in the middle.
        {
            eval += 15;
            if (move.TargetSquare.File == 7 || move.TargetSquare.File == 6 || move.TargetSquare.File == 0 || move.TargetSquare.File == 1) eval -= 60;
            if (move.TargetSquare.File == 2 || move.TargetSquare.File == 3 || move.TargetSquare.File == 4 || move.TargetSquare.File == 5) if (move.TargetSquare.Rank == 2 || move.TargetSquare.Rank == 3 || move.TargetSquare.Rank == 4 || move.TargetSquare.Rank == 5) eval += 45;
        }
        if (move.MovePieceType == PieceType.Bishop)// Bishop piece square table
        {
            if (piecesLeftnow > 28) eval -= 30;
            eval += 15;
            if (move.TargetSquare.File == 2 || move.TargetSquare.File == 3 || move.TargetSquare.File == 4 || move.TargetSquare.File == 5)
            {
                if (move.TargetSquare.Rank == 2 || move.TargetSquare.Rank == 3 || move.TargetSquare.Rank == 4 || move.TargetSquare.Rank == 5) eval += 45;
            }
        }
        if (move.MovePieceType == PieceType.Rook)// Rook piece square table + transition to endgame
        {
            if (board.IsWhiteToMove) { if (move.TargetSquare.Rank == 7) eval += 40; }
            else if (move.TargetSquare.Rank == 2) eval += 40;
            if (move.TargetSquare.File == 3 || move.TargetSquare.File == 4) eval += 30;
            if (piecesLeftnow > 28) eval -= 30;
            eval -= 20;
        }
        if (move.MovePieceType == PieceType.Queen)// Queen piece square table + transition to mid/endgame
        {
            if (piecesLeftnow < 14) eval += 25;
            else eval -= 90;
            if (move.TargetSquare.File == 2 || move.TargetSquare.File == 3 || move.TargetSquare.File == 4 || move.TargetSquare.File == 5)
            {
                if (move.TargetSquare.Rank == 2 || move.TargetSquare.Rank == 3 || move.TargetSquare.Rank == 4 || move.TargetSquare.Rank == 5) eval += 45;
            }
        }

        if (move.MovePieceType == PieceType.Pawn)// Pawn "piece square table" This is mainly for early game
        {
            if (movesSinceLastPawnMove >= 25) eval += 25;
            if (piecesLeftnow < 14 || piecesLeftnow > 28) eval += 10;
            if (move.TargetSquare.File == 4 || move.TargetSquare.File == 5) eval += 30;
            eval += 5;
            if (piecesLeftnow < 8) eval += 70;
        }

        if (move.IsCastles) eval += 50; // castling is encouraged

        // We're out of the piece square tables!
        // This is for the flags to buff certain moves and nerf others
        // e.g. Checkmate is the highest priority move tied with en passant
        // Drawing is discouraged massively.
        // Checks are encouraged.
        // Moving away a piece that is attack is encouraged heavily.
        // Promotions are worth sacrificing a rook

        if (moveIsCheckmate(board, move)) eval = 999999999;
        if (moveHasBeenPlayed(board, move)) eval -= 1000;
        if (moveIsCheck(board, move)) eval += 20;
        if (board.SquareIsAttackedByOpponent(move.StartSquare)) eval += 120;
        if (move.IsPromotion) eval += 600;

        int currentDist = kingDSTfromOpponentKing(board);
        board.MakeMove(move);
        int newDist = kingDSTfromOpponentKing(board);
        board.UndoMove(move);
        if (piecesLeftnow < 6)
        {
            if (newDist < currentDist) eval += 99;
        }
        else if (piecesLeftnow < 10) eval += 55;
        foreach (Move move2 in GetEnemyMoves(board))
        {
            if (moveIsCheckmate(board, move2)) eval -= 10000000;
            if (moveIsCheck(board, move2)) eval -= 60;
            if (moveHasBeenPlayed(board, move2)) eval -= 50000;
            if (move2.IsCapture) eval -= pieceVals[(int)move2.CapturePieceType];
            if (GetEnemyMoves(board).Length == 1) eval += 80;
        }

        return eval;


    }



    public Move Think(Board board, Timer timer)
    {
        Move[] moves = board.GetLegalMoves();
        Move moveToPlay = moves[0];
        int bestEvaluation = -999999;
        foreach (Move move in moves)
        {
            if (evaluateMove(move, board) > bestEvaluation)
            {
                bestEvaluation = evaluateMove(move, board);
                moveToPlay = move;
            }
        }
        if (moveToPlay.MovePieceType == PieceType.Pawn) movesSinceLastPawnMove = 0;
        else movesSinceLastPawnMove++;
        return moveToPlay;
    }
}