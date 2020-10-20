﻿using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixiEditor.Models.Layers;
using PixiEditor.Models.Position;

namespace PixiEditor.Models.ImageManipulation
{
    public static class BitmapUtils
    {
        /// <summary>
        ///     Converts pixel bytes to WriteableBitmap
        /// </summary>
        /// <param name="currentBitmapWidth">Width of bitmap</param>
        /// <param name="currentBitmapHeight">Height of bitmap</param>
        /// <param name="byteArray">Bitmap byte array</param>
        /// <returns>WriteableBitmap</returns>
        public static WriteableBitmap BytesToWriteableBitmap(int currentBitmapWidth, int currentBitmapHeight,
            byte[] byteArray)
        {
            var bitmap = BitmapFactory.New(currentBitmapWidth, currentBitmapHeight);
            bitmap.FromByteArray(byteArray);
            return bitmap;
        }

        /// <summary>
        ///     Converts layers bitmaps into one bitmap.
        /// </summary>
        /// <param name="layers">Layers to combine</param>
        /// <param name="width">Width of final bitmap</param>
        /// <param name="height">Height of final bitmap</param>
        /// <returns>WriteableBitmap of layered bitmaps</returns>
        public static WriteableBitmap CombineLayers(Layer[] layers, int width, int height)
        {
            var finalBitmap = BitmapFactory.New(width, height);

            using (finalBitmap.GetBitmapContext())
            {
                for (var i = 0; i < layers.Length; i++)
                for (var y = 0; y < finalBitmap.Height; y++)
                for (var x = 0; x < finalBitmap.Width; x++)
                {
                    var color = layers[i].GetPixelWithOffset(x, y);
                    color = Color.FromArgb((byte) (color.A * layers[i].Opacity), color.R, color.G, color.B);
                    if (color.A != 0 || color.R != 0 || color.B != 0 || color.G != 0) finalBitmap.SetPixel(x, y, color);
                }
            }

            return finalBitmap;
        }

        public static Dictionary<Layer, Color[]> GetPixelsForSelection(Layer[] layers, Coordinates[] selection)
        {
            var result = new Dictionary<Layer, Color[]>();

            for (var i = 0; i < layers.Length; i++)
            {
                var pixels = new Color[selection.Length];

                using (layers[i].LayerBitmap.GetBitmapContext())
                {
                    for (var j = 0; j < pixels.Length; j++)
                    {
                        var position = layers[i].GetRelativePosition(selection[j]);
                        if (position.X < 0 || position.X > layers[i].Width - 1 || position.Y < 0 ||
                            position.Y > layers[i].Height - 1)
                            continue;
                        pixels[j] = layers[i].GetPixel(position.X, position.Y);
                    }
                }


                result[layers[i]] = pixels;
            }

            return result;
        }
    }
}