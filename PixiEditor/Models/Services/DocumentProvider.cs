﻿using PixiEditor.Models.Controllers;
using PixiEditor.Models.DataHolders;
using PixiEditor.Models.Layers;
using System.Collections.Generic;

namespace PixiEditor.Models.Services
{
    /// <summary>
    /// Provides the active document and it's values like active layer and reference layer
    /// </summary>
    public class DocumentProvider
    {
        private readonly BitmapManager _bitmapManager;

        public DocumentProvider(BitmapManager bitmapManager)
        {
            _bitmapManager = bitmapManager;
        }

        /// <summary>
        /// Gets all opened documents
        /// </summary>
        public IEnumerable<Document> GetDocuments() => _bitmapManager.Documents;

        /// <summary>
        /// Gets the active document
        /// </summary>
        public Document GetDocument() => _bitmapManager.ActiveDocument;

        /// <summary>
        /// Get the layers of the opened document
        /// </summary>
        public IEnumerable<Layer> GetLayers() => _bitmapManager.ActiveDocument?.Layers;

        /// <summary>
        /// Gets the active layer
        /// </summary>
        public Layer GetLayer() => _bitmapManager.ActiveLayer;

        /// <summary>
        /// Gets the surface of the active layer
        /// </summary>
        public Surface GetSurface() => _bitmapManager.ActiveLayer?.LayerBitmap;

        /// <summary>
        /// Gets the reference layer of the active document
        /// </summary>
        public Layer GetReferenceLayer() => _bitmapManager.ActiveDocument?.ReferenceLayer;

        public Surface GetReferenceSurface() => _bitmapManager.ActiveDocument?.ReferenceLayer?.LayerBitmap;

        /// <summary>
        /// Gets the renderer for the active document
        /// </summary>
        public LayerStackRenderer GetRenderer() => _bitmapManager.ActiveDocument?.Renderer;

        /// <summary>
        /// Gets the renderer for the reference layer of the active document
        /// </summary>
        public SingleLayerRenderer GetReferenceRenderer() => _bitmapManager.ActiveDocument?.ReferenceLayerRenderer;
    }
}
