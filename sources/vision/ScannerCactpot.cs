﻿using MgAl2O4.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace FFTriadBuddy
{
    public class ScannerCactpot : ScannerBase
    {
        public class GameState : GameStateBase
        {
            public int[] board;
            public int numRevealed;

            public GameState()
            {
                board = new int[9] { 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                numRevealed = 0;
            }
        }

        private Rectangle cachedBoard;
        private Rectangle[] cachedCircles;

        public GameState cachedGameState;

        private FastPixelMatch colorMatchBack = new FastPixelMatchMono(0, 80);
        private FastPixelMatch colorMatchCircleFade = new FastPixelMatchHueMono(30, 60, 40, 120);
        private FastPixelMatch colorMatchCircleOut = new FastPixelMatchHueMono(30, 60, 80, 255);
        private FastPixelMatch colorMatchCircleOutH = new FastPixelMatchHueMono(170, 200, 80, 255);
        private FastPixelMatch colorMatchCircleIn = new FastPixelMatchMono(110, 255);
        private FastPixelMatch colorMatchNumber = new FastPixelMatchMono(0, 110);

        public override void InvalidateCache()
        {
            cachedBoard = Rectangle.Empty;
            cachedCircles = null;
            cachedGameState = null;
        }

        public override bool HasValidCache(FastBitmapHSV bitmap, int scannerFlags)
        {
            return (cachedBoard.Width > 0) && (cachedCircles != null) && HasCactpotMatch(bitmap, cachedBoard.Left, cachedBoard.Top, cachedBoard.Width / 3);
        }

        public Rectangle GetBoardBox() { return cachedBoard; }
        public Rectangle GetCircleBox(int idx) { return cachedCircles[idx]; }

        public override void AppendDebugShapes(List<Rectangle> shapes)
        {
            base.AppendDebugShapes(shapes);
            if (cachedCircles != null) { shapes.AddRange(cachedCircles); }
        }

        public override bool DoWork(FastBitmapHSV bitmap, int scannerFlags, Stopwatch perfTimer, bool debugMode)
        {
            base.DoWork(bitmap, scannerFlags, perfTimer, debugMode);
            perfTimer.Restart();

            if (!HasValidCache(bitmap, scannerFlags))
            {
                cachedCircles = null;
                cachedBoard = FindCactpotCoords(bitmap);

                if (cachedBoard.Width > 0)
                {
                    screenAnalyzer.currentScanArea = cachedBoard;
                    cachedCircles = FindCactpotCircleCoords(cachedBoard);
                }
            }

            cachedGameState = null;
            if (cachedCircles != null)
            {
                cachedGameState = new GameState();
                cachedGameStateBase = cachedGameState;
                for (int Idx = 0; Idx < cachedGameState.board.Length; Idx++)
                {
                    cachedGameState.board[Idx] = ParseCactpotCircle(bitmap, cachedCircles[Idx], "board" + Idx);
                    cachedGameState.numRevealed += (cachedGameState.board[Idx] != 0) ? 1 : 0;
                }
            }

            perfTimer.Stop();
            if (debugMode) { Logger.WriteLine("Parse cactpot board: " + perfTimer.ElapsedMilliseconds + "ms"); }
            return cachedGameState != null;
        }

        #region Image scan

        private Rectangle FindCactpotCoords(FastBitmapHSV bitmap)
        {
            Rectangle rect = new Rectangle();

            // find number fields
            //
            // scan method similar to triple triad board with matching top left corner and tile size

            int minCellSize = bitmap.Width * 2 / 100;
            int maxCellSize = bitmap.Width * 6 / 100;
            int maxScanX = bitmap.Width - (maxCellSize * 3) - 20;
            int maxScanY = bitmap.Height - (maxCellSize * 2) - 20;

            //HasCactpotCircleMatch(bitmap, 521, 497, 67, out bool dummyFlag1, true);

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            bool[] highlights = new bool[9];
            bool bScanning = true;
            for (int IdxY = 0; IdxY < maxScanY && bScanning; IdxY++)
            {
                int ScanY = (bitmap.Height / 2) + ((IdxY / 2) * ((IdxY % 2 == 0) ? 1 : -1));
                for (int IdxX = 10; IdxX < maxScanX && bScanning; IdxX++)
                {
                    if (HasCactpotCircleEdgeV(bitmap, IdxX, ScanY, -1))
                    {
                        for (int cellSize = minCellSize; cellSize < maxCellSize && bScanning; cellSize++)
                        {
                            if (HasCactpotCellMatch(bitmap, IdxX, ScanY, cellSize, out int CellPosX, out int CellPosY))
                            {
                                if (HasCactpotCircleMatch(bitmap, CellPosX, CellPosY, cellSize))
                                {
                                    if (HasCactpotMatch(bitmap, CellPosX, CellPosY, cellSize))
                                    {
                                        rect = new Rectangle(CellPosX, CellPosY, cellSize * 3, cellSize * 3);

                                        bScanning = false;
                                        break;
                                    }
                                    else
                                    {
                                        // skip all nearby matches on X, will still hit all nearby on Y
                                        IdxX += 3;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            stopwatch.Stop();
            if (debugMode) { Logger.WriteLine("FindCactpotCoords: " + stopwatch.ElapsedMilliseconds + "ms"); }

            return rect;
        }

        private bool HasCactpotCircleMatch(FastBitmapHSV bitmap, int posX, int posY, int cellSize, bool bDebugDetection = false)
        {
            int sizeA = cellSize;
            int offsetB = 5;
            int sizeB = sizeA - (offsetB * 2);

            Point[] testPoints = new Point[4];
            int numHighlights = 0;

            // 4 points: corners of cell => dark background
            {
                testPoints[0].X = posX; testPoints[0].Y = posY;
                testPoints[1].X = posX + sizeA; testPoints[1].Y = posY;
                testPoints[2].X = posX; testPoints[2].Y = posY + sizeA;
                testPoints[3].X = posX + sizeA; testPoints[3].Y = posY + sizeA;

                for (int Idx = 0; Idx < 4; Idx++)
                {
                    FastPixelHSV testPx = bitmap.GetPixel(testPoints[Idx].X, testPoints[Idx].Y);
                    //if (bDebugDetection) { Logger.WriteLine("[" + posX + ", " + posY + "] testing A[" + testPoints[Idx].X + "," + testPoints[Idx].Y + "]: " + testPx); }

                    if (!colorMatchBack.IsMatching(testPx))
                    {
                        if (bDebugDetection) { Logger.WriteLine("[" + posX + ", " + posY + "] failed, not background: A[" + testPoints[Idx].X + "," + testPoints[Idx].Y + "]: " + testPx); }
                        return false;
                    }
                }
            }

            // 4 points: corners with offset B => dark background
            {
                testPoints[0].X = posX + offsetB; testPoints[0].Y = posY + offsetB;
                testPoints[1].X = posX + sizeA - offsetB; testPoints[1].Y = posY + offsetB;
                testPoints[2].X = posX + offsetB; testPoints[2].Y = posY + sizeA - offsetB;
                testPoints[3].X = posX + sizeA - offsetB; testPoints[3].Y = posY + sizeA - offsetB;

                for (int Idx = 0; Idx < 4; Idx++)
                {
                    FastPixelHSV testPx = bitmap.GetPixel(testPoints[Idx].X, testPoints[Idx].Y);
                    //if (bDebugDetection) { Logger.WriteLine("[" + posX + ", " + posY + "] testing B1[" + testPoints[Idx].X + "," + testPoints[Idx].Y + "]: " + testPx); }

                    if (!colorMatchBack.IsMatching(testPx))
                    {
                        if (bDebugDetection) { Logger.WriteLine("[" + posX + ", " + posY + "] failed, not background: B1[" + testPoints[Idx].X + "," + testPoints[Idx].Y + "]: " + testPx); }
                        return false;
                    }
                }
            }

            // 4 points: midpoints with offset B => yellow/white
            {
                testPoints[0].X = posX + offsetB; testPoints[0].Y = posY + (sizeA / 2);
                testPoints[1].X = posX + sizeA - offsetB; testPoints[1].Y = posY + (sizeA / 2);
                testPoints[2].X = posX + (sizeA / 2); testPoints[2].Y = posY + offsetB;
                testPoints[3].X = posX + (sizeA / 2); testPoints[3].Y = posY + sizeA - offsetB;

                for (int Idx = 0; Idx < 4; Idx++)
                {
                    FastPixelHSV testPx = bitmap.GetPixel(testPoints[Idx].X, testPoints[Idx].Y);
                    //if (bDebugDetection) { Logger.WriteLine("[" + posX + ", " + posY + "] testing B2[" + testPoints[Idx].X + "," + testPoints[Idx].Y + "]: " + testPx); }

                    if (colorMatchCircleOutH.IsMatching(testPx))
                    {
                        numHighlights++;
                    }
                    else if (!colorMatchCircleOut.IsMatching(testPx))
                    {
                        if (bDebugDetection) { Logger.WriteLine("[" + posX + ", " + posY + "] failed, not circle: B2[" + testPoints[Idx].X + "," + testPoints[Idx].Y + "]: " + testPx); }
                        return false;
                    }
                }
            }

            // 4 points: midpoints of diagonal lines between midpoints B (C) => yellow/white 
            {
                testPoints[0].X = posX + offsetB + (sizeB / 4); testPoints[0].Y = posY + offsetB + (sizeB / 4);
                testPoints[1].X = posX + sizeA - offsetB - (sizeB / 4); testPoints[1].Y = posY + offsetB + (sizeB / 4);
                testPoints[2].X = posX + offsetB + (sizeB / 4); testPoints[2].Y = posY + sizeA - offsetB - (sizeB / 4);
                testPoints[3].X = posX + sizeA - offsetB - (sizeB / 4); testPoints[3].Y = posY + sizeA - offsetB - (sizeB / 4);

                for (int Idx = 0; Idx < 4; Idx++)
                {
                    FastPixelHSV testPx = bitmap.GetPixel(testPoints[Idx].X, testPoints[Idx].Y);
                    //if (bDebugDetection) { Logger.WriteLine("[" + posX + ", " + posY + "] testing B3[" + testPoints[Idx].X + "," + testPoints[Idx].Y + "]: " + testPx); }

                    if (!colorMatchCircleIn.IsMatching(testPx))
                    {
                        if (bDebugDetection) { Logger.WriteLine("[" + posX + ", " + posY + "] failed, not circle: B3[" + testPoints[Idx].X + "," + testPoints[Idx].Y + "]: " + testPx); }
                        return false;
                    }
                }
            }

            return (numHighlights == 0) || (numHighlights == 4);
        }

        private bool HasCactpotMatch(FastBitmapHSV bitmap, int posX, int posY, int cellSize, bool bDebugDetection = false)
        {
            if (bDebugDetection) { Logger.WriteLine("HasCactpotMatch[" + posX + ", " + posY + "]? testing..."); }

            // circle at (posX, posY) already matched, test other 8
            bool bHasMatch = true;
            bool bHasMatch01 = (bHasMatch || bDebugDetection) && HasCactpotCircleMatch(bitmap, posX + cellSize, posY, cellSize);
            bHasMatch = bHasMatch && bHasMatch01;
            bool bHasMatch02 = (bHasMatch || bDebugDetection) && HasCactpotCircleMatch(bitmap, posX + (cellSize * 2), posY, cellSize);
            bHasMatch = bHasMatch && bHasMatch02;

            bool bHasMatch10 = (bHasMatch || bDebugDetection) && HasCactpotCircleMatch(bitmap, posX, posY + cellSize, cellSize);
            bHasMatch = bHasMatch && bHasMatch10;
            bool bHasMatch11 = (bHasMatch || bDebugDetection) && HasCactpotCircleMatch(bitmap, posX + cellSize, posY + cellSize, cellSize);
            bHasMatch = bHasMatch && bHasMatch11;
            bool bHasMatch12 = (bHasMatch || bDebugDetection) && HasCactpotCircleMatch(bitmap, posX + (cellSize * 2), posY + cellSize, cellSize);
            bHasMatch = bHasMatch && bHasMatch12;

            bool bHasMatch20 = (bHasMatch || bDebugDetection) && HasCactpotCircleMatch(bitmap, posX, posY + (cellSize * 2), cellSize);
            bHasMatch = bHasMatch && bHasMatch20;
            bool bHasMatch21 = (bHasMatch || bDebugDetection) && HasCactpotCircleMatch(bitmap, posX + cellSize, posY + (cellSize * 2), cellSize);
            bHasMatch = bHasMatch && bHasMatch21;
            bool bHasMatch22 = (bHasMatch || bDebugDetection) && HasCactpotCircleMatch(bitmap, posX + (cellSize * 2), posY + (cellSize * 2), cellSize);
            bHasMatch = bHasMatch && bHasMatch22;

            if (bDebugDetection)
            {
                Logger.WriteLine(">> [#" + (bHasMatch01 ? "#" : ".") + (bHasMatch02 ? "#" : ".") +
                    "][" + (bHasMatch10 ? "#" : ".") + (bHasMatch11 ? "#" : ".") + (bHasMatch12 ? "#" : ".") +
                    "][" + (bHasMatch20 ? "#" : ".") + (bHasMatch21 ? "#" : ".") + (bHasMatch22 ? "#" : ".") +
                    "] => " + (bHasMatch ? "match" : "nope"));
            }

            return bHasMatch;
        }

        private bool HasCactpotCircleEdgeV(FastBitmapHSV bitmap, int posX, int posY, int sideX, bool bDebugDetection = false)
        {
            const int spreadY = 2;
            const int offsetDeepX = 5;

            FastPixelHSV testPx = bitmap.GetPixel(posX, posY);
            if (!colorMatchCircleOut.IsMatching(testPx))
            {
                if (bDebugDetection) { Logger.WriteLine("HasCactpotCircleEdgeV[" + posX + "," + posY + "] failed: edge center " + testPx); }
                return false;
            }

            testPx = bitmap.GetPixel(posX - (sideX * offsetDeepX), posY);
            if (!colorMatchCircleOut.IsMatching(testPx))
            {
                if (bDebugDetection) { Logger.WriteLine("HasCactpotCircleEdgeV[" + posX + "," + posY + "] failed: deep center " + testPx); }
                return false;
            }

            testPx = bitmap.GetPixel(posX, posY - spreadY);
            if (!colorMatchCircleOut.IsMatching(testPx))
            {
                if (bDebugDetection) { Logger.WriteLine("HasCactpotCircleEdgeV[" + posX + "," + posY + "] failed: edge top " + testPx); }
                return false;
            }

            testPx = bitmap.GetPixel(posX, posY + spreadY);
            if (!colorMatchCircleOut.IsMatching(testPx))
            {
                if (bDebugDetection) { Logger.WriteLine("HasCactpotCircleEdgeV[" + posX + "," + posY + "] failed: edge bottom " + testPx); }
                return false;
            }

            testPx = bitmap.GetPixel(posX + sideX, posY);
            if (!colorMatchBack.IsMatching(testPx) && !colorMatchCircleFade.IsMatching(testPx))
            {
                if (bDebugDetection) { Logger.WriteLine("HasCactpotCircleEdgeV[" + posX + "," + posY + "] failed: edge center prev " + testPx); }
                return false;
            }

            testPx = bitmap.GetPixel(posX + sideX, posY - spreadY);
            if (!colorMatchBack.IsMatching(testPx) && !colorMatchCircleFade.IsMatching(testPx))
            {
                if (bDebugDetection) { Logger.WriteLine("HasCactpotCircleEdgeV[" + posX + "," + posY + "] failed: edge top prev " + testPx); }
                return false;
            }

            testPx = bitmap.GetPixel(posX + sideX, posY + spreadY);
            if (!colorMatchBack.IsMatching(testPx) && !colorMatchCircleFade.IsMatching(testPx))
            {
                if (bDebugDetection) { Logger.WriteLine("HasCactpotCircleEdgeV[" + posX + "," + posY + "] failed: edge bottom prev " + testPx); }
                return false;
            }

            return true;
        }

        private bool HasCactpotCellMatch(FastBitmapHSV bitmap, int posX, int posY, int cellSize, out int cellPosX, out int cellPosY, bool bDebugDetection = false)
        {
            cellPosX = 0;
            cellPosY = 0;

            bool bHasEdgeV2 = HasCactpotCircleEdgeV(bitmap, posX + cellSize, posY, -1);
            if (bDebugDetection) { Logger.WriteLine("HasCactpotCellMatch[" + posX + ", " + posY + "], cellSize:" + cellSize + " => " + (bHasEdgeV2 ? "found V2" : "nope")); }

            if (bHasEdgeV2)
            {
                for (int Idx = 5; Idx < 20; Idx++)
                {
                    int invPosX = posX + cellSize - Idx;
                    bool bHasEdgeV3 = HasCactpotCircleEdgeV(bitmap, invPosX, posY, 1);
                    if (bHasEdgeV3)
                    {
                        if (bDebugDetection) { Logger.WriteLine(">> spacing check: V3 at X:" + invPosX); }

                        cellPosX = posX - (Idx / 2);
                        cellPosY = posY - (cellSize / 2);

                        return true;
                    }
                }
            }

            return false;
        }

        private Rectangle[] FindCactpotCircleCoords(Rectangle cactpotBox)
        {
            Rectangle[] circleBoxes = new Rectangle[9];
            int cellSize = cactpotBox.Width / 3;

            for (int IdxY = 0; IdxY < 3; IdxY++)
            {
                for (int IdxX = 0; IdxX < 3; IdxX++)
                {
                    circleBoxes[IdxX + (IdxY * 3)] = new Rectangle(cactpotBox.Left + (IdxX * cellSize), cactpotBox.Top + (IdxY * cellSize), cellSize, cellSize);
                }
            }

            return circleBoxes;
        }

        private int ParseCactpotCircle(FastBitmapHSV bitmap, Rectangle circleBox, string debugName)
        {
            int result = 0;

            int innerOffsetX = circleBox.Width / 3;
            int innerOffsetY = circleBox.Height / 4;
            Rectangle numberBox = new Rectangle(circleBox.Left + innerOffsetX, circleBox.Top + innerOffsetY, circleBox.Width - (innerOffsetX * 2), circleBox.Height - (innerOffsetY * 2));
            if (debugMode) { debugShapes.Add(numberBox); }

            float numberPct = ImageUtils.CountFillPct(bitmap, numberBox, colorMatchNumber);
            if (numberPct > 0.05f)
            {
                if (debugMode) { Logger.WriteLine("ParseCactpotCircle[" + debugName + "] has number inside"); }

                List<Point> spanV = ImageUtils.TraceSpansV(bitmap, numberBox, colorMatchNumber, 5);
                foreach (Point span in spanV)
                {
                    Point boundsH = ImageUtils.TraceBoundsH(bitmap, new Rectangle(numberBox.Left, span.X, numberBox.Width, span.Y), colorMatchNumber, span.Y * 2);
                    if (boundsH.Y >= 4)
                    {
                        Rectangle hashBox = new Rectangle(boundsH.X, span.X, boundsH.Y, span.Y);

                        FastBitmapHash cactpotHashData = ImageUtils.CalculateImageHash(bitmap, hashBox, circleBox, colorMatchNumber, 8, 16, true);

                        CactpotNumberHash bestNumberOb = (CactpotNumberHash)ImageUtils.FindMatchingHash(cactpotHashData, EImageHashType.Cactpot, out int bestDistance, debugMode);
                        if (bestNumberOb == null)
                        {
                            if (ImageUtils.ConditionalAddUnknownHash(
                                ImageUtils.CreateUnknownImageHash(cactpotHashData, screenAnalyzer.screenReader.cachedScreenshot, EImageHashType.Cactpot), screenAnalyzer.unknownHashes))
                            {
                                screenAnalyzer.OnUnknownHashAdded();
                            }
                        }
                        else
                        {
                            cactpotHashData.GuideOb = bestNumberOb;
                            screenAnalyzer.currentHashDetections.Add(cactpotHashData, bestDistance);

                            result = bestNumberOb.number;
                        }

                        debugHashes.Add(cactpotHashData);
                    }
                }
            }

            return result;
        }

        #endregion

        #region Validation

        public override void ValidateScan(string configPath, ScreenAnalyzer.EMode mode)
        {
            string testName = Path.GetFileNameWithoutExtension(configPath);

            GameState validationState = LoadValidationConfig(configPath);
            if (validationState == null || cachedGameState == null)
            {
                string exceptionMsg = string.Format("Test {0} failed! Scan results:{1}, config path: {2}", testName, cachedGameState, configPath);
                throw new Exception(exceptionMsg);
            }

            for (int idx = 0; idx < 9; idx++)
            {
                if (cachedGameState.board[idx] != validationState.board[idx])
                {
                    string exceptionMsg = string.Format("Test {0} failed! Board[{1}] got:{2}, expected:{3}",
                        testName, idx,
                        cachedGameState.board[idx],
                        validationState.board[idx]);
                    throw new Exception(exceptionMsg);
                }
            }
        }

        private GameState LoadValidationConfig(string configPath)
        {
            GameState configData = new GameState();
            string configText = File.ReadAllText(configPath);

            JsonParser.ObjectValue rootOb = JsonParser.ParseJson(configText);

            JsonParser.ArrayValue ruleArr = rootOb.entries["board"] as JsonParser.ArrayValue;
            configData.board = new int[ruleArr.entries.Count];
            for (int idx = 0; idx < ruleArr.entries.Count; idx++)
            {
                configData.board[idx] = ruleArr.entries[idx] as JsonParser.IntValue;
            }

            return configData;
        }

        #endregion
    }
}
