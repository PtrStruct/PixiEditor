﻿using PixiEditor.Helpers;
using PixiEditor.Models.Controllers;
using PixiEditor.Models.DataHolders;
using PixiEditor.Models.Enums;
using PixiEditor.Models.ImageManipulation;
using PixiEditor.Models.IO;
using PixiEditor.Models.Layers;
using PixiEditor.Models.Position;
using PixiEditor.Models.Undo;
using PixiEditor.ViewModels;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Transform = PixiEditor.Models.ImageManipulation.Transform;

namespace PixiEditor.Models.Tools.Tools
{
    public class MoveTool : BitmapOperationTool
    {
        private static readonly SKPaint maskingPaint = new()
        {
            BlendMode = SKBlendMode.DstIn,
        };

        private static readonly SKPaint inverseMaskingPaint = new()
        {
            BlendMode = SKBlendMode.DstOut,
        };

        private Layer[] affectedLayers;
        private Surface[] currentlyDragged;
        private Coordinates[] currentlyDraggedPositions;
        private Surface previewLayerData;

        private List<Coordinates> moveStartSelectedPoints = null;
        private Coordinates moveStartPos;
        private Int32Rect moveStartRect;

        private Coordinates lastDragDelta;

        private StorageBasedChange change;

        private string defaultActionDisplay = "Hold mouse to move selected pixels. Hold Ctrl to move all layers.";

        public MoveTool(BitmapManager bitmapManager)
        {
            ActionDisplay = defaultActionDisplay;
            Cursor = Cursors.Arrow;
            RequiresPreviewLayer = true;
            UseDefaultUndoMethod = false;

            BitmapManager = bitmapManager;
        }

        public override string Tooltip => "Moves selected pixels (V). Hold Ctrl to move all layers.";

        public override bool HideHighlight => true;

        private BitmapManager BitmapManager { get; }

        public override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl)
            {
                ActionDisplay = "Hold mouse to move all layers.";
            }
        }

        public override void OnKeyUp(KeyEventArgs e)
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl)
            {
                ActionDisplay = defaultActionDisplay;
            }
        }

        public override void AddUndoProcess(Document document)
        {
            var args = new object[] { change.Document };
            document.UndoManager.AddUndoChange(change.ToChange(UndoProcess, args));
            if (moveStartSelectedPoints != null)
            {
                SelectionHelpers.AddSelectionUndoStep(document, moveStartSelectedPoints, SelectionType.New);
                document.UndoManager.SquashUndoChanges(3, "Move selected area");
                moveStartSelectedPoints = null;
            }
            change = null;
        }

        private void UndoProcess(Layer[] layers, UndoLayer[] data, object[] args)
        {
            if (args.Length > 0 && args[0] is Document document)
            {
                for (int i = 0; i < layers.Length; i++)
                {
                    Layer layer = layers[i];
                    document.Layers.RemoveAt(data[i].LayerIndex);

                    document.Layers.Insert(data[i].LayerIndex, layer);
                    if (data[i].IsActive)
                    {
                        document.SetMainActiveLayer(data[i].LayerIndex);
                    }
                }

            }
        }

        public override void OnStart(Coordinates startPos)
        {
            Document doc = BitmapManager.ActiveDocument;
            Selection selection = doc.ActiveSelection;
            bool anySelection = selection.SelectedPoints.Any();

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                affectedLayers = doc.Layers.Where(x => x.IsVisible).ToArray();
            }
            else
            {
                affectedLayers = doc.Layers.Where(x => x.IsActive && doc.GetFinalLayerIsVisible(x)).ToArray();
            }

            change = new StorageBasedChange(doc, affectedLayers, true);

            Layer selLayer = selection.SelectionLayer;
            moveStartRect = anySelection ? 
                new(selLayer.OffsetX, selLayer.OffsetY, selLayer.Width, selLayer.Height) :
                new (0, 0, doc.Width, doc.Height);
            moveStartPos = startPos;
            lastDragDelta = new Coordinates(0, 0);

            previewLayerData?.Dispose();
            previewLayerData = CreateCombinedPreview(anySelection ? selLayer : null, affectedLayers);

            if (currentlyDragged != null)
            {
                foreach (var surface in currentlyDragged)
                    surface.Dispose();
            }

            if (anySelection)
            {
                currentlyDragged = ExtractDraggedPortions(anySelection ? selLayer : null, affectedLayers);
                currentlyDraggedPositions = Enumerable.Repeat(new Coordinates(selLayer.OffsetX, selLayer.OffsetY), affectedLayers.Length).ToArray();
            }
            else
            {
                (currentlyDraggedPositions, currentlyDragged) = CutDraggedLayers(affectedLayers);
            }

            if (anySelection)
                moveStartSelectedPoints = selection.SelectedPoints.ToList();
        }

        private Surface CreateCombinedPreview(Layer selLayer, Layer[] layersToCombine)
        {
            var combined = BitmapUtils.CombineLayers(moveStartRect, layersToCombine, BitmapManager.ActiveDocument.LayerStructure);
            if (selLayer != null)
            {
                using var selSnap = selLayer.LayerBitmap.SkiaSurface.Snapshot();
                combined.SkiaSurface.Canvas.DrawImage(selSnap, 0, 0, maskingPaint);
            }
            return combined;
        }

        private static (Coordinates[], Surface[]) CutDraggedLayers(Layer[] draggedLayers)
        {
            Surface[] outSurfaces = new Surface[draggedLayers.Length];
            Coordinates[] outCoords = new Coordinates[draggedLayers.Length];

            int count = 0;
            foreach (var layer in draggedLayers)
            {
                outCoords[count] = new Coordinates(layer.OffsetX, layer.OffsetY);
                Surface copy = new(layer.Width, layer.Height);
                layer.LayerBitmap.SkiaSurface.Draw(copy.SkiaSurface.Canvas, 0, 0, Surface.ReplacingPaint);
                layer.LayerBitmap.SkiaSurface.Canvas.Clear();
                layer.InvokeLayerBitmapChange();
                outSurfaces[count] = copy;
                count++;
            }

            return (outCoords, outSurfaces);
        }

        private static Surface[] ExtractDraggedPortions(Layer selLayer, Layer[] draggedLayers)
        {
            using var selSnap = selLayer.LayerBitmap.SkiaSurface.Snapshot();
            Surface[] output = new Surface[draggedLayers.Length];
            
            int count = 0;
            foreach (Layer layer in draggedLayers)
            {
                Surface portion = new Surface(selLayer.Width, selLayer.Height);
                SKRect selLayerRect = new SKRect(0, 0, selLayer.Width, selLayer.Height);

                int x = selLayer.OffsetX - layer.OffsetX;
                int y = selLayer.OffsetY - layer.OffsetY;

                using (var layerSnap = layer.LayerBitmap.SkiaSurface.Snapshot())
                    portion.SkiaSurface.Canvas.DrawImage(layerSnap, new SKRect(x, y, x + selLayer.Width, y + selLayer.Height), selLayerRect, Surface.ReplacingPaint);
                portion.SkiaSurface.Canvas.DrawImage(selSnap, 0, 0, maskingPaint);
                output[count] = portion;
                count++;

                layer.LayerBitmap.SkiaSurface.Canvas.DrawImage(selSnap, new SKRect(0, 0, selLayer.Width, selLayer.Height), 
                    new SKRect(selLayer.OffsetX - layer.OffsetX, selLayer.OffsetY - layer.OffsetY, selLayer.OffsetX - layer.OffsetX + selLayer.Width, selLayer.OffsetY - layer.OffsetY + selLayer.Height), 
                    inverseMaskingPaint);
                layer.InvokeLayerBitmapChange(new Int32Rect(selLayer.OffsetX, selLayer.OffsetY, selLayer.Width, selLayer.Height));
            }
            return output;
        }

        public override void Use(Layer layer, List<Coordinates> mouseMove, SKColor color)
        {
            Coordinates newPos = mouseMove[0];
            int dX = newPos.X - moveStartPos.X;
            int dY = newPos.Y - moveStartPos.Y;
            BitmapManager.ActiveDocument.ActiveSelection.TranslateSelection(dX - lastDragDelta.X, dY - lastDragDelta.Y);
            lastDragDelta = new Coordinates(dX, dY);


            int newX = moveStartRect.X + dX;
            int newY = moveStartRect.Y + dY;
            
            layer.DynamicResizeAbsolute(newX + moveStartRect.Width, newY + moveStartRect.Height, newX, newY);
            previewLayerData.SkiaSurface.Draw(layer.LayerBitmap.SkiaSurface.Canvas, newX - layer.OffsetX, newY - layer.OffsetY, Surface.ReplacingPaint);
            layer.InvokeLayerBitmapChange(new Int32Rect(newX, newY, moveStartRect.Width, moveStartRect.Height));
        }

        public override void OnStoppedRecordingMouseUp(MouseEventArgs e)
        {
            base.OnStoppedRecordingMouseUp(e);

            BitmapManager.ActiveDocument.PreviewLayer.ClearCanvas();

            ApplySurfacesToLayers(currentlyDragged, currentlyDraggedPositions, affectedLayers, new Coordinates(lastDragDelta.X, lastDragDelta.Y));
            foreach (var surface in currentlyDragged)
                surface.Dispose();
            currentlyDragged = null;
        }

        private static void ApplySurfacesToLayers(Surface[] surfaces, Coordinates[] startPositions, Layer[] layers, Coordinates delta)
        {
            int count = 0;
            foreach (Surface surface in surfaces)
            {
                var layer = layers[count];
                using SKImage snapshot = surface.SkiaSurface.Snapshot();
                Coordinates position = new Coordinates(startPositions[count].X + delta.X, startPositions[count].Y + delta.Y);
                layer.DynamicResizeAbsolute(position.X + surface.Width, position.Y + surface.Height, position.X, position.Y);
                layer.LayerBitmap.SkiaSurface.Canvas.DrawImage(snapshot, position.X - layer.OffsetX, position.Y - layer.OffsetY);
                layer.InvokeLayerBitmapChange(new Int32Rect(position.X, position.Y, surface.Width, surface.Height));

                count++;
            }
        }
    }
}
