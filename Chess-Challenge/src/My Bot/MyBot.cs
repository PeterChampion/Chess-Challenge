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
    private int turnCount, ourTotalPieceValue, opponentTotalPieceValue, turnsSinceLastPawnMove;
    private int pieceValueDelta => ourTotalPieceValue - opponentTotalPieceValue;
    private bool inEndGame;

    private Board currentBoard;
    private string moveLogicReason = string.Empty;
    private string bestMoveReason = string.Empty;

    private enum ChessOpening { RuyLopez, London, Sicilian, CaroKann }
    private ChessOpening chosenOpening;

    /*
     * TODO:
     * Cache current piece value for ourselves and opponent - Done
     * Determine whether a move is a safe capture - Done
     * Determine whether the opponent has a safe capture - Done
     * Determine whether we defend a previously under-defended piece (Would no longer lose an exchange) - DONE
     * Determine when endgame is in play (Under threshhold of piece value on either side) - DONE
     * Seek draws if losing significantly (If our move IS a draw, or if it creates more chances of our opponent causing a draw!) - PARTIALLY DONE
     * Promote if safe (square is defended and we win exchange) Ensure to treat the promoted piece as a pawn and not a Queen etc! - PARTIALLY DONE
     * Encourage in end game moves that push the opposing King towards the edges of the board. - TBD
     * Encourage in end game moving our pawns towards the end of the board - TBD
     * Encourage in end game moving our King towards center of the board OR towards our pawns (Unless they are close to the end of the board and can promote safely?) - TBD
     * Encourage moves that attack opposing pieces in a safe manner - DONE
     * Calculate sacrifices! (If this move loses us a piece, but we can then immediately take a piece of HIGHER value safely the next turn) - TBD
     * Pick from a select range of Chess openings when playing White/Black - Done
     */

    // White Openings:
    // Ruy Lopez - Pawn E4, Bishop B5, Knight F3
    // Queens Gambit - Pawn E4, Pawn C4

    // Black Openings:
    // Sicilian Defense - Pawn C5, Pawn D6
    // Caro-Kann - Pawn C6, Pawn D5, Pawn E6

    /// <summary>
    /// Return a move after thinking about and evaluating the current position.
    /// </summary>
    public Move Think(Board board, Timer timer)
    {
        currentBoard = board;
        turnCount += 1;

        if (turnCount == 1)
        {
            chosenOpening = board.IsWhiteToMove ? (ChessOpening)new Random().Next(0, 2) : (ChessOpening)new Random().Next(2, 4);
            Console.WriteLine($"We are playing as {(board.IsWhiteToMove ? "White" : "Black")} and have chosen to play the {chosenOpening}!");
        }

        var ourPrimaryPieceCount = 0;
        var opponentsPrimaryPieceCount = 0;

        for (int i = 0; i < 64; i++)
        {
            var piece = board.GetPiece(new Square(i));

            if (piece.PieceType != PieceType.Knight && piece.PieceType != PieceType.Bishop && piece.PieceType != PieceType.Rook && piece.PieceType != PieceType.Queen)            
                continue;

            if (piece.IsWhite == board.IsWhiteToMove)
                ourPrimaryPieceCount++;
            else
                opponentsPrimaryPieceCount++;
        }

        inEndGame = ourPrimaryPieceCount <= 3 && opponentsPrimaryPieceCount <= 3;

        /// Force the bot to delay its move for testing purposes.
        //System.Threading.Thread.Sleep(2500);

        return FindBestMoveOnBoard(board);
    }

    private Move FindBestMoveOnBoard(Board board)
    {
        var bestMoveEvaluation = int.MinValue;
        Move bestMove = new();

        /// Cache our and our opponents total piece values on the board.
        ourTotalPieceValue = GetTotalPieceValues(board, board.IsWhiteToMove);
        opponentTotalPieceValue = GetTotalPieceValues(board, !board.IsWhiteToMove);

        /// Iterate over all possible moves.
        foreach (var move in board.GetLegalMoves())
        {
            /// Evaluate the current move.
            var moveScore = EvaluateMove(board, move);

            if (moveScore > bestMoveEvaluation)
            {
                if (!bestMove.IsNull)
                {
                    Console.WriteLine($"The move {move} has a score of {moveScore} which is better than the previous best move {bestMove} which had a score of {bestMoveEvaluation}");
                    Console.WriteLine(moveLogicReason + "\n");
                }

                bestMoveReason = moveLogicReason;
                bestMoveEvaluation = moveScore;
                bestMove = move;
            }
        }

        turnsSinceLastPawnMove = bestMove.MovePieceType == PieceType.Pawn ? 0 : turnsSinceLastPawnMove++;

        Console.WriteLine($"\n\nCHOSE THE MOVE {bestMove} WITH THE FOLLOWING REASONS:\n{bestMoveReason}\n");
        return bestMove;
    }

    /// <summary>
    /// Return the current total value of all pieces of the passed colour.
    /// </summary>
    private int GetTotalPieceValues(Board board, bool isWhite)
    {
        var totalPieceValue = 0;

        /// Iterate over every square on the board.
        var i = 64;
        while (i-- > 0)
        {
            /// Cache the piece if its matches the colour passed.
            var piece = board.GetPiece(new Square(i));
            var pieceValue = pieceValues[(int)piece.PieceType];
            if (piece.IsWhite == isWhite)
                totalPieceValue += pieceValue;
        }

        return totalPieceValue;
    }

    /// <summary>
    /// Evaluates the given move and assigns it an 'evalScore' based on various parameters such as whether the move is checkmate, en passant, check, capture, castles, moves to a dangerous/risky square, etc.
    /// </summary>
    private int EvaluateMove(Board board, Move move)
    {
        var evalScore = 0;
        var opponentKingSquare = board.GetKingSquare(board.IsWhiteToMove);
        var kingDistanceFromPiece = GetKingsDistanceFromSquare(board, move.StartSquare, board.IsWhiteToMove);
        var kingDistancesPreMove = GetKingsDistanceFromSquare(board, opponentKingSquare, board.IsWhiteToMove);
        var targetSquare = move.TargetSquare;
        var startSquare = move.StartSquare;
        var movePieceType = move.MovePieceType;
        var pieceValue = pieceValues[(int)movePieceType];
        moveLogicReason = string.Empty;

        // The below is an idea for a different kind of evaluation method:
        ///// TODO: Evaluate and score the CURRENT board state.
        //var currentBoardScore = EvaluateBoardState(board, board.IsWhiteToMove);
        ///// TODO: Evaluate and score the RESULTING board state from this move.
        //var nextBoardScore = EvaluateBoardStatePostMove(board, move, board.IsWhiteToMove);
        ///// TODO: Return the inversed delta of the two scores.

        /// Always play mate in 1 or En Passant (Which is a moral checkmate).
        if (DoesMoveResultInCheckmate(board, move))
        {
            moveLogicReason += $"\n Move is checkmate (+{int.MaxValue - 1})";
            return int.MaxValue - 1;
        }
        else if (move.IsEnPassant)
        {
            moveLogicReason += $"\n Move is En Passant!! (+{int.MaxValue})";
            return int.MaxValue;
        }

        /// Immediately look for a different move if this move would result in a draw, and we are not losing by 2 pieces.
        if (DoesMoveResultInDraw(board, move) && pieceValueDelta >= -600)
            return int.MinValue;

        /// Encourage moves to a safe position if our current one is under threat (Not defended sufficiently).
        var isStartSquareSafe = IsSquareSafe(board, startSquare);
        var isTargetSquareSafe = IsMoveSafe(board, move, out var moveLogic);
        moveLogicReason += $"\n{moveLogic}";

        if (!isStartSquareSafe && isTargetSquareSafe)
        {
            moveLogicReason += $"\n Move goes from an unsafe space to a safe space. (+50)";
            evalScore += 50;
        }

        /// Encourage captures by the value of the captured piece minus the value of the attacking piece / 10 to encourage taking with lower value pieces first in exchanges.
        var moveIsSafeCapture = IsMoveSafeCapture(move, out var capturedPieceValue, out var _);
        if (moveIsSafeCapture)
        {
            if (capturedPieceValue == 100 && ((!board.IsWhiteToMove && move.TargetSquare.Rank == 6) || (board.IsWhiteToMove && move.TargetSquare.Rank == 1)))            
                capturedPieceValue = 900;            

            moveLogicReason += $"\n Move captures a {board.GetPiece(move.TargetSquare)} piece safely. (+{capturedPieceValue - (pieceValue / 10)})";
            evalScore += capturedPieceValue - (pieceValue / 10);
        }

        /// Encourage moves that open up opportunitys to take the opponents piece next move.
        var movePosturesSafeCapture = DoesMovePosturesSafeCapture(board, move, out var potentialNextCapture);
        if (movePosturesSafeCapture && isTargetSquareSafe)
        {
            moveLogicReason += $"\n Move postures to safely capture a {potentialNextCapture} next turn. (+{(pieceValues[(int)potentialNextCapture] / 2) - (pieceValue / 5)})";
            evalScore += (pieceValues[(int)potentialNextCapture] / 2) - (pieceValue / 5);
        }

        /// Encourage moves that defend pieces that are under threat
        var movePreventsSafeCapture = DoesMovePreventSafeCaptures(board, move, out var protectedSquares);
        if (movePreventsSafeCapture)
        {
            moveLogicReason += $"\n Move postures to protects the squares {string.Join("-", protectedSquares)} which could have been safely taken next turn. (+???)";

            foreach (var square in protectedSquares)
                evalScore += pieceValues[(int)board.GetPiece(square).PieceType] - (pieceValue / 10);
        }

        /// Discourage moves that would result in us missing a safe capture opportunity of the largest capturable piece.
        var moveMissesSafeCapture = false;
        var highestMissedCapture = capturedPieceValue;
        Move missedCaptureMove = new Move();
        foreach (var otherMove in board.GetLegalMoves(true))
        {
            if (otherMove.TargetSquare == move.TargetSquare)
                continue;

            if (IsMoveSafeCapture(otherMove, out capturedPieceValue, out var _))
            {
                if (capturedPieceValue > highestMissedCapture)
                {
                    moveMissesSafeCapture = true;
                    missedCaptureMove = otherMove;
                    highestMissedCapture = capturedPieceValue;
                }
            }
        }

        if (moveMissesSafeCapture)
        {
            moveLogicReason += $"\n Move misses the safe capture of {missedCaptureMove.StartSquare.Name}{missedCaptureMove.TargetSquare.Name} (-{highestMissedCapture / 2})";
            evalScore -= highestMissedCapture / 2;
        }

        /// Discourage moves with the King that would prevent us from castling.
        if ((board.HasKingsideCastleRight(board.IsWhiteToMove) || board.HasQueensideCastleRight(board.IsWhiteToMove)) && !move.IsCastles && movePieceType == PieceType.King)
        {
            moveLogicReason += $"\n Move disqualifys us from castling. (-150)";
            evalScore -= 150;
        }

        /// If the move is being made with a Rook whilst we still have the rights to castle...
        if (movePieceType == PieceType.Rook)
        {
            var castlingOptions = 0;

            if (board.HasKingsideCastleRight(board.IsWhiteToMove))
                castlingOptions++;

            if (board.HasQueensideCastleRight(board.IsWhiteToMove))
                castlingOptions++;

            if (castlingOptions == 2)
            {
                if (moveIsSafeCapture)
                {
                    moveLogicReason += $"\n Move disqualifys us from castling on one side but captures a piece. (-35)";
                    evalScore -= 35;
                }    
                else
                {
                    moveLogicReason += $"\n Move disqualifys us from castling on one side but still allows castling on the other. (-75)";
                    evalScore -= 75;
                }
            }
            else if (castlingOptions == 1)
            {
                if (moveIsSafeCapture)
                {
                    moveLogicReason += $"\n Move disqualifys us from castling on either side but captures a piece. (-75)";
                    evalScore -= 75;
                }
                else
                {
                    moveLogicReason += $"\n Move disqualifys us from castling on either side. (-150)";
                    evalScore -= 150;
                }
            }
        }

        /// Encourage pawn moves that move to a safe position
        if (move.MovePieceType == PieceType.Pawn && isTargetSquareSafe)
        {
            moveLogicReason += $"\n Moves advances a pawn to a safe square. (+25)";
            evalScore += 25;
        }

        /// Cache piece value threatened on both sides before and after suggested move.
        var ourTotalThreatenedPieceValuePreMove = GetTotalPieceValueThreatened(board, new Move(), false);
        var ourTotalThreatedPieceValuePostMove = GetTotalPieceValueThreatened(board, move, false);
        var opponentsTotalThreatenedPieceValuePreMove = GetTotalPieceValueThreatened(board, new Move(), true);
        var opponentsTotalThreatedPieveValuePostMove = GetTotalPieceValueThreatened(board, move, true);

        /// Encourage moves that help reduce our total piece value under threat.
        if (ourTotalThreatedPieceValuePostMove < ourTotalThreatenedPieceValuePreMove && !isTargetSquareSafe)
        {
            moveLogicReason += $"\n Move safely blocks a total value of {ourTotalThreatenedPieceValuePreMove - ourTotalThreatedPieceValuePostMove} from being safely captured! (+{(int)((ourTotalThreatenedPieceValuePreMove - ourTotalThreatedPieceValuePostMove) / 1.5f) - pieceValue})";
            evalScore += (int)((ourTotalThreatenedPieceValuePreMove - ourTotalThreatedPieceValuePostMove) / 1.5f) - pieceValue;
        }

        // We already do the below technically via checking if we posture for safe captures?
        /// Encourage moves that increase our opponents piece value under threat.
        //if (opponentsTotalThreatedPieveValuePostMove > opponentsTotalThreatenedPieceValuePreMove && !isTargetSquareSafe)
        //{
        //    moveLogicReason += $"\n Move safely increases a total value of {ourTotalThreatenedPieceValuePreMove - ourTotalThreatedPieceValuePostMove} from being safely captured! (+{(int)((ourTotalThreatenedPieceValuePreMove - ourTotalThreatedPieceValuePostMove) / 1.5f) - pieceValue})";
        //    evalScore += (int)((ourTotalThreatenedPieceValuePreMove - ourTotalThreatedPieceValuePostMove) / 1.5f) - pieceValue;
        //}

        //board.MakeMove(move);
        //var newPieceValueDelta = ourTotalPieceValue - GetTotalPieceValues(board, board.IsWhiteToMove);
        //if (pieceValueDelta < newPieceValueDelta)
        //{
        //    moveLogicReason += $"\n Move increases piece value delta by {newPieceValueDelta - pieceValueDelta}";
        //    evalScore += (newPieceValueDelta - pieceValueDelta) / 2;
        //}
        //board.UndoMove(move);

        var movePutsInCheck = DoesMoveResultInCheck(board, move);
        /// Encourage checking the opponent outside of the opening.
        if (turnCount >= 8 && movePutsInCheck && isTargetSquareSafe)
        {
            moveLogicReason += $"\n Move results in a check outside of the opening. (+50)";
            evalScore += 50;
        }

        var kingDistancesPostMove = GetKingsDistanceFromSquare(board, opponentKingSquare, board.IsWhiteToMove);

        /// In the event of a safe capture being possible with a pawn, but the move would result in stacked pawns, only encourage by half the standard value.
        /// This will increase the odds of another piece capturing instead of the pawn, if possible.
        if (move.IsCapture && movePieceType == PieceType.Pawn)
        {
            var createsStackedPawn = MoveCreatesStackedPawn(board, move);

            if (createsStackedPawn && IsMoveSafe(board, move, out var _))
            {
                var otherPieceCanSafelyCapture = false;

                foreach (var potentialMove in board.GetLegalMoves(true))
                {
                    if (potentialMove.TargetSquare != move.TargetSquare || potentialMove == move)
                        continue;

                    otherPieceCanSafelyCapture = IsMoveSafe(board, potentialMove, out var _);

                    if (otherPieceCanSafelyCapture)
                        break;
                }

                if (otherPieceCanSafelyCapture)
                {
                    moveLogicReason += $"\n Move creates stacked pawns when another piece can safely capture (-500)";
                    evalScore -= 500;
                }
                else
                {
                    moveLogicReason += $"\n Move creates stacked pawns. (-50)";
                    evalScore -= 50;
                }
            }
        }
        /// Discourage repeated positions that aren't checks.
        if (board.IsRepeatedPosition() && !movePutsInCheck)
        {
            moveLogicReason += $"\n Move is a repeated position that is not a check. (-75)";
            evalScore -= 75;
        }

        /// Discourage draws if winning in overall piece value, encourage if we are losing by a significant margin however (Can't win 'em all!)
        if (DoesMoveResultInDraw(currentBoard, move))
        {
            moveLogicReason += $"\n Move results in a draw.";
            evalScore += pieceValueDelta >= -600 ? -9999 : 9999;
        }

        /// Assess the opponents best 1 move responses.
        AssessOpponentResponses(board, move, ref evalScore);

        /// Encourage castling.
        if (move.IsCastles)
        {
            moveLogicReason += $"\n Move castles. (+125)";
            evalScore += 125;
        }

        /// Opening turns logic.
        if (turnCount <= 10)
        {
            switch (movePieceType)
            {
                default:
                case PieceType.None:
                    break;
                case PieceType.Pawn:
                    break;
                case PieceType.Knight:
                case PieceType.Bishop:
                //case PieceType.Rook:
                    /// Encourage moving Knight/Bishop/Rook pieces if they are on their starting rank within the first few moves.
                    if ((board.IsWhiteToMove && move.StartSquare.Rank == 0 && move.TargetSquare.Rank != 0) || (!board.IsWhiteToMove && move.StartSquare.Rank == 7 && move.TargetSquare.Rank != 7))
                    {
                        moveLogicReason += $"\n Move takes a {board.GetPiece(move.StartSquare)} away from its starting rank. (+50)";
                        evalScore += 50;
                    }
                    break;

                case PieceType.Queen:
                    /// If the Queen is not under attack, discourage moves with it.
                    if (!board.SquareIsAttackedByOpponent(move.StartSquare))
                    {
                        moveLogicReason += $"\n Move would move a safe Queen in the opening. (-100)";
                        evalScore -= 100;
                    }
                    else
                    {
                        moveLogicReason += $"\n Move would move a unsafe Queen in the opening. (+200)";
                        evalScore += 200;
                    }
                    break;
                case PieceType.King:
                    break;
            }

            /// Opening logic switch
            switch (chosenOpening)
            {
                case ChessOpening.RuyLopez:
                    if (turnCount == 1 && move.StartSquare.Name == "e2" && move.TargetSquare.Name == "e4")
                    {
                        moveLogicReason += $"\n Move is from our opening. (+150)";
                        evalScore += 150;
                    }
                    else if (turnCount == 2 && move.StartSquare.Name == "f1" && move.TargetSquare.Name == "b5")
                    {
                        moveLogicReason += $"\n Move is from our opening. (+150)";
                        evalScore += 150;
                    }
                    else if (turnCount == 3 && move.StartSquare.Name == "g1" && move.TargetSquare.Name == "f3")
                    {
                        moveLogicReason += $"\n Move is from our opening. (+150)";
                        evalScore += 150;
                    }
                    break;
                case ChessOpening.London:
                    if (turnCount == 1 && move.StartSquare.Name == "d2" && move.TargetSquare.Name == "d4")
                    {
                        moveLogicReason += $"\n Move is from our opening. (+150)";
                        evalScore += 150;
                    }
                    else if (turnCount == 2 && move.StartSquare.Name == "c1" && move.TargetSquare.Name == "f4")
                    {
                        moveLogicReason += $"\n Move is from our opening. (+150)";
                        evalScore += 150;
                    }
                    else if (turnCount == 3 && move.StartSquare.Name == "e2" && move.TargetSquare.Name == "e3")
                    {
                        moveLogicReason += $"\n Move is from our opening. (+150)";
                        evalScore += 150;
                    }
                    break;
                case ChessOpening.Sicilian:
                    if (turnCount == 1 && move.StartSquare.Name == "c7" && move.TargetSquare.Name == "c5")
                    {
                        moveLogicReason += $"\n Move is from our opening. (+150)";
                        evalScore += 150;
                    }
                    else if (turnCount == 2 && move.StartSquare.Name == "d7" && move.TargetSquare.Name == "d6")
                    {
                        moveLogicReason += $"\n Move is from our opening. (+150)";
                        evalScore += 150;
                    }
                    break;
                case ChessOpening.CaroKann:
                    if (turnCount == 1 && move.StartSquare.Name == "c7" && move.TargetSquare.Name == "c6")
                    {
                        moveLogicReason += $"\n Move is from our opening. (+150)";
                        evalScore += 150;
                    }
                    else if (turnCount == 2 && move.StartSquare.Name == "d7" && move.TargetSquare.Name == "d5")
                    {
                        moveLogicReason += $"\n Move is from our opening. (+150)";
                        evalScore += 150;
                    }
                    else if (turnCount == 3 && move.StartSquare.Name == "e7" && move.TargetSquare.Name == "e6")
                    {
                        moveLogicReason += $"\n Move is from our opening. (+150)";
                        evalScore += 150;
                    }
                    break;
            }
        }

        /// Discourage pawns next to the King from moving.
        if (movePieceType == PieceType.Pawn)
        {
            if (kingDistanceFromPiece <= 1)
            {
                moveLogicReason += $"\n Move pushes a pawn near our King. (-50)";
                evalScore -= 50;
            }
            else
            {
                moveLogicReason += $"\n Move pushes a pawn. (+15)";
                evalScore += 15;
            }

            /// Encourage a pawn move if one hasn't been made for a while.
            if (turnsSinceLastPawnMove >= 10)
            {
                moveLogicReason += $"\n Move pushes a pawn after not moving one for 10 turns. (+75)";
                evalScore += 75;
            }
        }

        /// Discourage Knights moving to the edge of the board
        if (move.MovePieceType == PieceType.Knight)
        {
            if (targetSquare.File == 0 || targetSquare.File == 7 || targetSquare.Rank == 0 || targetSquare.Rank == 7)
            {
                moveLogicReason += $"\n Move results in a Knight on the edge of the Board. (-50)";
                evalScore -= 25;
            }
            else if ((targetSquare.File == 3 || targetSquare.File == 4) && (targetSquare.Rank >= 2 && targetSquare.Rank <= 5))
            {
                moveLogicReason += $"\n Move results in a Knight in the centre of the Board. (+25)";
                evalScore += 25;
            }
        }

        /// Encourage moving a non pawn piece that is on its starting file
        if (movePieceType != PieceType.Pawn && move.MovePieceType != PieceType.King && ((board.IsWhiteToMove && startSquare.Rank == 0 && targetSquare.Rank != 0) || (!board.IsWhiteToMove && startSquare.Rank == 7 && targetSquare.Rank != 7)))
        {
            moveLogicReason += $"\n Moves a piece that's on its starting rank. (+25)";
            evalScore += 25;
        }

        /// Encourage safe promotions to a Queen.
        if (IsSquareSafe(board, move.TargetSquare) && move.IsPromotion)
        {
            if (move.PromotionPieceType != PieceType.Queen)
                evalScore -= 1000;
            else
            {
                moveLogicReason += $"\n Move safely promotes a pawn. (+300)";
                evalScore += 300;
            }
        }

        /// King moves that aren't castling.
        if (movePieceType == PieceType.King && !move.IsCastles)
        {
            /// Encourage moves towards the opposing King if we are in the end game.
            if (inEndGame)
            {
                if (kingDistancesPostMove < kingDistancesPreMove)
                {
                    moveLogicReason += $"\n Move pushes our King towards the opponent. (+50)";
                    evalScore += 50;
                }
                /// Otherwise if we aren't in the endgame, discourage moving the King towards the opponent.
                else
                {
                    moveLogicReason += $"\n Move pushes our King towards the opponent. (-50)";
                    evalScore -= 50;
                }
            }
        }

        var ourKingSafety = GetKingSafety(board, false);
        var ourKingSafetyPostMove = GetKingSafetyPostMove(board, move, false);
        var opponentKingSafety = GetKingSafety(board, true);
        var opponentKingSafetyPostMove = GetKingSafetyPostMove(board, move, true);

        /// If our King has less possible moves, the move is safe and we are not in the End Game, we consider this as him being safer.
        if (ourKingSafetyPostMove < ourKingSafety && isTargetSquareSafe && !inEndGame && move.MovePieceType == PieceType.King)
        {
            moveLogicReason += $"\n Move improves OUR King safety. (+25)";
            evalScore += 25;
        }

        /// If our opponents King has less possible moves and the move is safe, we consider them to be in an unsafe position.
        if (opponentKingSafetyPostMove < opponentKingSafety && isTargetSquareSafe)
        {
            moveLogicReason += $"\n Move decreases OPPONENTS King safety. (+25)";
            evalScore += 25;
        }

        var boardControlScore = GetBoardControlScore(board);
        var boardControlScorePostMove = GetBoardControlScorePostMove(board, move);

        /// Encourage safely increasing our control of the other side of board.
        if (boardControlScorePostMove > boardControlScore && isTargetSquareSafe && movePieceType != PieceType.King)
        {
            moveLogicReason += $"\n Move increases our control of the board (+25)";
            evalScore += 25;
        }

        /// Encourage moves to a defended square.
        var movesToDefendedSquare = IsSquareDefended(board, move.TargetSquare, move.StartSquare);
        if (movesToDefendedSquare && movePieceType != PieceType.King)
        {
            moveLogicReason += $"\n Moves piece to a defended square. (+25)";
            evalScore += 25;
        }

        // TODO: Instead calculate if this move increases OVERALL piece mobility, this seems much better?
        /// Encourage move if it increases this pieces mobility.
        var pieceLegalMovesCount = GetPieceLegalMoves(board, move.StartSquare);
        var pieceLegalMovesCountPostMove = GetPieceLegalMovesPostMove(board, move);
        if (pieceLegalMovesCountPostMove > pieceLegalMovesCount && isTargetSquareSafe)
        {
            moveLogicReason += $"\n Moves to a square with more options. (+50)";
            evalScore += 50;
        }
        
        /// Encourage moves that posture a next move checkmate.
        var movePosturesCheckmate = DoesMovePostureCheckmate(board, move);
        if (movePosturesCheckmate && isTargetSquareSafe)
        {
            moveLogicReason += $"\n Move postures a potential checkmate. (+250)";
            evalScore += 250;
        }

        // TODO: Encourage moves that prevent the opponent from being able to promote a pawn?
        var canOpponentPromote = CanPromoteNextMove(board, true);
        var canOpponentPromotePostMove = CanPromoteNextMove(board, true);
        if (canOpponentPromote && !canOpponentPromotePostMove)
        {
            moveLogicReason += $"\n Move prevents the opponent from promoting a pawn next move. (+300)";
            evalScore += 300;
        }

        return evalScore;
    }

    private bool CanPromoteNextMove(Board board, bool opponent)
    {
        var canPromote = false;

        if (opponent)
            board.ForceSkipTurn();

        foreach (var move in board.GetLegalMoves())
        {
            canPromote = move.IsPromotion;

            if (canPromote)
                break;
        }

        if (opponent)
            board.UndoSkipTurn();

        return canPromote;
    }

    private bool DoesMovePostureCheckmate(Board board, Move move)
    {
        board.MakeMove(move);
        board.ForceSkipTurn();
        var checkmateFound = false;

        foreach (var legalMove in board.GetLegalMoves())
        {
            board.MakeMove(legalMove);
            checkmateFound = board.IsInCheckmate();
            board.UndoMove(legalMove);

            if (checkmateFound)
                break;
        }

        board.UndoSkipTurn();
        board.UndoMove(move);

        return checkmateFound;
    }

    private int GetPieceLegalMoves(Board board, Square pieceSquare)
    {
        var pieceLegalMoveCount = 0;

        foreach (var move in board.GetLegalMoves())        
            if (move.StartSquare == pieceSquare)
                pieceLegalMoveCount++;
        
        return pieceLegalMoveCount;
    }

    private int GetPieceLegalMovesPostMove(Board board, Move move)
    {
        board.MakeMove(move);
        var pieceLegalMoveCount = GetPieceLegalMoves(board, move.TargetSquare);
        board.UndoMove(move);
        return pieceLegalMoveCount;
    }

    private int GetTotalPieceValueThreatened(Board board, Move move, bool opponents)
    {
        var pieceValueThreatened = 0;
        if (!move.IsNull)
            board.MakeMove(move);
        else if (!opponents)
            board.ForceSkipTurn();

        foreach (var opponentMove in board.GetLegalMoves(true))        
            if (IsMoveSafeCapture(opponentMove, out var capturedPieceValue, out var moveLogic) && opponentMove.TargetSquare != move.StartSquare)            
                pieceValueThreatened += capturedPieceValue;

        if (!move.IsNull)
            board.UndoMove(move);
        else if (!opponents)
            board.UndoSkipTurn();

        return pieceValueThreatened;
    }

    private void AssessOpponentResponses(Board board, Move move, ref int currentEvaluation)
    {
        /// Make OUR move, so that we can look at the potential board state from the opponents perspective.
        board.MakeMove(move);

        /// Assess our opponents immediate next move which could follow this one.
        foreach (var opponentMove in board.GetLegalMoves())
        {
            var opponentHasSafeMove = IsMoveSafe(board, opponentMove, out var _);

            /// Discourage move if our opponent can safely make a capture.            
            if (IsMoveSafeCapture(opponentMove, out var ourCapturedPieceValue, out var moveLogic) && ourCapturedPieceValue > 0)
            {
                // TODO: Check if by capturing this piece, the opponent leaves themselves open to either a Checkmate, or losing a higher value piece
                // In which case this move should be ENCOURAGED instead!

                moveLogicReason += $"\n Move allows our opponent to make the move {opponentMove} which captures our {board.GetPiece(opponentMove.TargetSquare)} piece safely. (-{ourCapturedPieceValue / 2})";
                moveLogicReason += $"\n Reasoning: {moveLogic}";

                currentEvaluation -= ourCapturedPieceValue / 2;
            }

            //if (boardIsInCheck && (board.HasKingsideCastleRight(!isWhite) || board.HasQueensideCastleRight(!isWhite)) && !opponentMove.IsCastles && opponentMove.MovePieceType == PieceType.King)
            //    evalScore += 150;

            /// Discourage massively if the opponent could mate us in one.
            if (DoesMoveResultInCheckmate(board, opponentMove))
            {
                moveLogicReason += $"\n Move allows our opponent to checkmate us.";
                currentEvaluation -= 9999;
            }

            /// Discourage if the opponent can check us.
            if (DoesMoveResultInCheck(board, opponentMove) && opponentHasSafeMove)
            {
                moveLogicReason += $"\n Move allows our opponent to potentially check us safely with the move {opponentMove.StartSquare.Name} to {opponentMove.TargetSquare.Name}";
                currentEvaluation -= 100;
            }

            /// Discourage/Encourage possible draw positions and if we want those based on the delta of piece value.
            if (DoesMoveResultInDraw(currentBoard, opponentMove))
            {
                moveLogicReason += $"\n Move allows our opponent to cause a draw.";
                currentEvaluation += pieceValueDelta >= -600 ? -9999 : 9999;
            }
        }

        /// Return move to its original state.
        board.UndoMove(move);
    }

    private bool DoesMovePreventSafeCaptures(Board board, Move move, out List<Square> protectedSquares)
    {
        protectedSquares = new();
        var initialThreatenedSquares = new List<Square>();

        /// Skip our turn and assess our opponents potential capture.
        board.ForceSkipTurn();
        foreach (var legalOpponentCapture in board.GetLegalMoves(true))        
            if (legalOpponentCapture.TargetSquare != move.StartSquare && IsMoveSafeCapture(legalOpponentCapture, out var capturedPieceValue, out var _))            
                initialThreatenedSquares.Add(legalOpponentCapture.TargetSquare);

        /// Restore board state.
        board.UndoSkipTurn();

        /// Make our considered move on the board.
        board.MakeMove(move);

        /// Assess our opponents potential captures.
        var newThreatenedSquares = new List<Square>();
        foreach (var legalOpponentCapture in board.GetLegalMoves(true))
        {
            /// Record any squares that weren't under threat, but now are.
            if (IsMoveSafeCapture(legalOpponentCapture, out var _, out var _) && !initialThreatenedSquares.Contains(legalOpponentCapture.TargetSquare) && legalOpponentCapture.TargetSquare != move.StartSquare)
                newThreatenedSquares.Add(legalOpponentCapture.TargetSquare);
            /// Record any squares that were under threat, but no longer are.
            else if (!IsMoveSafeCapture(legalOpponentCapture, out var _, out var _) && initialThreatenedSquares.Contains(legalOpponentCapture.TargetSquare) && legalOpponentCapture.TargetSquare != move.StartSquare)            
                protectedSquares.Add(legalOpponentCapture.TargetSquare);            
        }

        board.UndoMove(move);

        /// Check for all opponent captures right now.
        /// Create a list of all of the squares they can SAFELY capture.
        /// Make this move.
        /// Create a separate list of all the squares the opponent can SAFELY capture once again.
        /// If the list has grown or not changed in size, return false.
        /// If the list is has reduced in size, attempt to remove all of the entries of the FIRST list from the SECOND.
        /// If the list is a size of 0, then we have successfully given our opponent less safe captures.
        /// To figure out the piece we defended specifically, we can remove the entries of the SECOND list from the FIRST, this will leave us with the PIECES defended 

        return protectedSquares.Count > 0 && newThreatenedSquares.Count == 0;
    }

    private bool DoesMovePosturesSafeCapture(Board board, Move move, out PieceType potentialNextCapture)
    {
        potentialNextCapture = PieceType.None;
        var highestValueCapture = 0;

        var isMoveSafe = IsMoveSafe(board, move, out var _);
        if (!isMoveSafe)
            return false;

        /// Make the move on the board.
        board.MakeMove(move);

        /// Skip the opponents turn (We are acting on the assumption they do not attempt to defend this piece.) 
        board.ForceSkipTurn();

        foreach (var legalCapture in board.GetLegalMoves(true))
        {
            if (legalCapture.StartSquare.Name == move.TargetSquare.Name)
            {
                if (IsMoveSafeCapture(legalCapture, out var capturedPieceValue, out var _))
                {
                    if (capturedPieceValue > highestValueCapture)
                    {
                        highestValueCapture = capturedPieceValue;
                        potentialNextCapture = board.GetPiece(legalCapture.TargetSquare).PieceType;
                    }
                }
            }
        }

        /// Restore board state.
        board.UndoSkipTurn();
        board.UndoMove(move);

        /// Return whether we are poised to capture anything safely.
        return potentialNextCapture != PieceType.None;
    }

    /// <summary>
    /// Returns whether the move safely captures an opposing piece, outing the value of the piece captured.
    /// </summary>
    private bool IsMoveSafeCapture(Move move, out int capturedPieceValue, out string moveLogic)
    {
        /// Cache the value of the piece at the move's target square.
        capturedPieceValue = pieceValues[(int)currentBoard.GetPiece(move.TargetSquare).PieceType];
        /// Return whether the square is safe, and captures a piece.
        var moveSafe = IsMoveSafe(currentBoard, move, out moveLogic);
        return capturedPieceValue > 0 && moveSafe;
    }

    /// <summary>
    /// Returns whether the passed move will result in a draw.
    /// </summary>
    private bool DoesMoveResultInDraw(Board board, Move move)
    {
        board.MakeMove(move);
        var isDraw = currentBoard.IsInsufficientMaterial() || (!currentBoard.IsInCheck() && currentBoard.GetLegalMoves().Length == 0) || currentBoard.FiftyMoveCounter >= 100;
        board.UndoMove(move);
        return isDraw;
    }

    /// <summary>
    /// Returns whether the passed move will result in a checked board state.
    /// </summary>
    private bool DoesMoveResultInCheck(Board board, Move move)
    {
        board.MakeMove(move);
        var inCheck = board.IsInCheck();
        board.UndoMove(move);
        return inCheck;
    }

    /// <summary>
    /// Returns whether the passed move will result in checkmate.
    /// </summary>
    private bool DoesMoveResultInCheckmate(Board board, Move move)
    {
        board.MakeMove(move);
        var isCheckmate = board.IsInCheckmate();
        board.UndoMove(move);
        return isCheckmate;
    }

    private bool IsSquareSafe(Board board, Square square)
    {
        board.ForceSkipTurn();
        foreach (var opponentMove in board.GetLegalMoves())
        {
            if (opponentMove.TargetSquare == square)
            {
                var isMoveSafe = IsMoveSafe(board, opponentMove, out var moveLogic);
                board.UndoSkipTurn();
                return !isMoveSafe;
            }
        }

        board.UndoSkipTurn();
        /// No attacks were able to reach the Square, therefore it is safe.
        return true;
    }

    /// <summary>
    /// Returns whether a move is safe, if the move targets a square the opponent can attack we return whether if an exchange of pieces was to occur if we lose less material, as if so the move is considered 'safe'.
    /// </summary>
    private bool IsMoveSafe(Board board, Move move, out string moveLogic)
    {
        moveLogic = string.Empty;

        /// If the opponent cannot attack the square we are moving to, this move is safe.
        if (!board.SquareIsAttackedByOpponent(move.TargetSquare))
            return true;

        List<int> ourPieceValues = new();
        int ourTotalPieceValue = 0;

        List<int> opponentPieceValues = new();
        int opponentTotalPieceValue = 0;

        /// Total all piece values we have targetting the square.
        foreach (var ourMove in currentBoard.GetLegalMoves())
        {
            if (ourMove.TargetSquare == move.TargetSquare)
            {
                ///// We don't consider moves that create stacked pawns as ways of defending.
                //if (ourMove.MovePieceType == PieceType.Pawn && MoveCreatesStackedPawn(board, ourMove))
                //    continue;

                var pieceValue = pieceValues[(int)ourMove.MovePieceType];
                ourPieceValues.Add(pieceValue);
                ourTotalPieceValue += pieceValue;
            }
        }

        /// Skip turn to get the moves of the opponent.
        board.MakeMove(move);

        /// Total all piece values defending the square.
        foreach (var opponentMove in currentBoard.GetLegalMoves())
        {
            if (opponentMove.TargetSquare == move.TargetSquare)
            {
                var pieceValue = pieceValues[(int)opponentMove.MovePieceType];
                opponentPieceValues.Add(pieceValue);
                opponentTotalPieceValue += pieceValue;
            }
        }

        /// Restore board state.
        board.UndoMove(move);

        /// Sort lists in ascending order.
        opponentPieceValues.Sort();
        ourPieceValues.Sort();

        /// If the move is a capture, we want to include the value of the piece that would be lost for our opponent.
        if (move.IsCapture)
        {
            var pieceValue = pieceValues[(int)board.GetPiece(move.TargetSquare).PieceType];
            
            // TODO: Only do the below if the opponents piece can promote next turn? (Square ahead is empty)
            /// Treat pawns that are on the verge of promotion as Queens.
            if (pieceValue == 100 && CanPromoteNextMove(board, true) && ((!board.IsWhiteToMove && move.TargetSquare.Rank == 6) || (board.IsWhiteToMove && move.TargetSquare.Rank == 1)))
                pieceValue = 900;

            /// Insert the piece as the first element in the collection as it will always be taken regardless of its value.
            opponentPieceValues.Insert(0, pieceValue);
            opponentTotalPieceValue += pieceValue;
        }

        /// If the opponent is looking at a square with EQUAL pieces OR MORE pieces and their pieces are of LOWER value - UNSAFE
        if (opponentPieceValues.Count == ourPieceValues.Count || (opponentPieceValues.Count >= ourPieceValues.Count && opponentTotalPieceValue <= ourTotalPieceValue))
        {
            moveLogic = $"CASE 1 Opponent has {opponentPieceValues.Count} pieces defending of {opponentTotalPieceValue} total value that would be lost in an exchange chain, whilst we have {ourPieceValues.Count} pieces attacking of {ourTotalPieceValue} total value.";
            return false;
        }
        /// If the opponent is looking at a square with LESS pieces, and their pieces are of HIGHER value - SAFE
        else if (opponentPieceValues.Count < ourPieceValues.Count && opponentTotalPieceValue >= ourTotalPieceValue)
        {
            moveLogic = $"CASE 2 Opponent has {opponentPieceValues.Count} pieces defending of {opponentTotalPieceValue} total value that would be lost in an exchange chain, whilst we have {ourPieceValues.Count} pieces attacking of {ourTotalPieceValue} total value.";
            return true;
        }
        /// If the opponent is looking at a square with EQUAL OR MORE pieces, and their pieces are of HIGHER value - DEPENDS - Exclude their pieces that would not be lost (x number of highest value pieces)
        else if (opponentPieceValues.Count >= ourPieceValues.Count && opponentTotalPieceValue > ourTotalPieceValue)
        {
            /// Recalculate total opponent piece value to be the amount of piece value that would be lost in this potential exchange.
            opponentTotalPieceValue = 0;
            for (int i = 0; i < ourPieceValues.Count; i++)            
                opponentTotalPieceValue += opponentPieceValues[i];

            moveLogic = $"CASE 3 Opponent has {opponentPieceValues.Count} pieces defending of {opponentTotalPieceValue} total value that would be lost in an exchange chain, whilst we have {ourPieceValues.Count} pieces attacking of {ourTotalPieceValue} total value.";

            /// Allow equal exchanges if we are ahead in material by 1 piece.
            //if (pieceValueDelta >= 300)
                return opponentTotalPieceValue >= ourTotalPieceValue;
            //else
                //return opponentTotalPieceValue > ourTotalPieceValue;
        }
        /// If the opponent is looking at a square with LESS pieces, and their pieces are of LOWER value - DEPENDS - Exclude our pieces that would not be lost (x number of highest value pieces)
        else if (opponentPieceValues.Count < ourPieceValues.Count && opponentTotalPieceValue < ourTotalPieceValue)
        {
            /// Recalculate our total piece value to be the amount of piece value that would be lost in this potential exchange.
            ourTotalPieceValue = 0;
            for (int i = 0; i < opponentPieceValues.Count; i++)
                ourTotalPieceValue += ourPieceValues[i];

            moveLogic = $"CASE 4 Opponent has {opponentPieceValues.Count} pieces defending of {opponentTotalPieceValue} total value that would be lost in an exchange chain, whilst we have {ourPieceValues.Count} pieces attacking of {ourTotalPieceValue} total value.";

            /// Allow equal exchanges if we are ahead in material by 1 piece.
            //if (pieceValueDelta >= 300)
                return opponentTotalPieceValue >= ourTotalPieceValue;
            //else
                //return opponentTotalPieceValue > ourTotalPieceValue;
        }

        throw new Exception($"Should not occur! We had {ourPieceValues.Count} pieces attacking and our opponent had {opponentPieceValues.Count} pieces - Our value was {ourTotalPieceValue} whilst our opponents was {opponentTotalPieceValue}");
    }

    /// <summary>
    /// Returns the distance of a given colours King from a specific square on the board.
    /// </summary>
    private int GetKingsDistanceFromSquare(Board board, Square targetSquare, bool isWhite)
    {
        var kingSquare = board.GetKingSquare(isWhite);
        return Math.Abs(kingSquare.File - targetSquare.File) + Math.Abs(kingSquare.Rank - targetSquare.Rank);
    }

    /// <summary>
    /// Does the move create a stacked pawn structure? (2 Pawns in the same File).
    /// </summary>
    private bool MoveCreatesStackedPawn(Board board, Move move)
    {
        /// TODO: Have this return the amount of stacked pawns (int) instead of a bool, so that we can ascertain whether the move UNSTACKS pawns as well.
        var currentStackedPawnCount = GetStackedPawnCount(board, board.IsWhiteToMove);
        board.MakeMove(move);
        var newStackedPawnCount = GetStackedPawnCount(board, !board.IsWhiteToMove);
        board.UndoMove(move);
        return newStackedPawnCount > currentStackedPawnCount;
    }

    /// <summary>
    /// Returns the current number of stacked pawns for a given colour.
    /// </summary>
    private int GetStackedPawnCount(Board board, bool isWhite)
    {
        var pawnPieceList = board.GetPieceList(PieceType.Pawn, isWhite).OrderBy(piece => piece.Square.File);
        var previousFile = -1;
        var stackedPawnCount = 0;

        foreach (var piece in pawnPieceList)
        {
            if (piece.Square.File == previousFile)
                stackedPawnCount++;

            previousFile = piece.Square.File;
        }

        return stackedPawnCount;
    }

    private int GetKingSafety(Board board, bool opponentsKing)
    {
        if (opponentsKing)
            board.ForceSkipTurn();

        var kingLegalMoveCount = 0;

        foreach (var move in board.GetLegalMoves())        
            if (move.MovePieceType == PieceType.King)
                kingLegalMoveCount++;

        if (opponentsKing)
            board.UndoSkipTurn();

        return kingLegalMoveCount;
    }

    private int GetKingSafetyPostMove(Board board, Move move, bool opponentsKing)
    {
        board.MakeMove(move); 
        board.ForceSkipTurn();
        var kingLegalMoveCount = GetKingSafety(board, opponentsKing);
        board.UndoSkipTurn();
        board.UndoMove(move);

        return kingLegalMoveCount;
    }

    private int GetBoardControlScore(Board board)
    {
        var boardControlScore = 0;
        HashSet<Square> controlledSquares = new();

        int[] scores = new int[8] { 1, 1, 1, 2, 3, 4, 5, 6};

        // TODO: Check if reverse is correct for White or not.
        if (!board.IsWhiteToMove)
            Array.Reverse(scores);

        /// Record all of the squares we can reach from our current legal moves + squares we occupy.
        foreach (var move in board.GetLegalMoves())
        {
            if (IsSquareSafe(board, move.StartSquare))
                controlledSquares.Add(move.StartSquare);

            if (IsMoveSafe(board, move, out var _))
                controlledSquares.Add(move.TargetSquare);
        }

        /// Calculate score based on the rank of each square.
        foreach (var square in controlledSquares)        
            boardControlScore += scores[square.Rank];        

        return boardControlScore;
    }

    private int GetBoardControlScorePostMove(Board board, Move move)
    {
        board.MakeMove(move);
        board.ForceSkipTurn();
        var boardControlScore = GetBoardControlScore(board);
        board.UndoSkipTurn();
        board.UndoMove(move);
        return boardControlScore;
    }

    private bool IsSquareDefended(Board board, Square targetSquare, Square startSquare)
    {
        if (!IsSquareSafe(board, targetSquare))
            return false;

        var squareDefended = false;

        foreach (var move in board.GetLegalMoves())
        {
            if (move.StartSquare == startSquare)
                continue;

            if (move.TargetSquare == targetSquare)
            {
                squareDefended = true;
                break;
            }
        }

        return squareDefended;
    }
}