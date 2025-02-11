﻿using PixiEditor.Helpers.Extensions;
using PixiEditor.Models.DataHolders;
using PixiEditor.Models.DataHolders.Palettes;
using PixiEditor.Models.IO;
using PixiEditor.Models.IO.JascPalFile;
using PixiEditor.Models.UserPreferences;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PixiEditor.Models.DataProviders
{
    public delegate void CacheUpdate(RefreshType refreshType, Palette itemAffected, string oldName);

    public class LocalPalettesFetcher : PaletteListDataSource
    {
        public static string PathToPalettesFolder { get; } = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PixiEditor", "Palettes");

        private List<Palette> cachedPalettes;

        public event CacheUpdate CacheUpdated;

        private List<string> cachedFavoritePalettes;

        private FileSystemWatcher watcher;

        public override void Initialize()
        {
            InitDir();
            watcher = new FileSystemWatcher(PathToPalettesFolder);
            watcher.Filter = "*.pal";
            watcher.Changed += FileSystemChanged;
            watcher.Deleted += FileSystemChanged;
            watcher.Renamed += RenamedFile;
            watcher.Created += FileSystemChanged;

            watcher.EnableRaisingEvents = true;
            cachedFavoritePalettes = IPreferences.Current.GetLocalPreference<List<string>>(PreferencesConstants.FavouritePalettes);

            IPreferences.Current.AddCallback(PreferencesConstants.FavouritePalettes, updated =>
            {
                cachedFavoritePalettes = (List<string>)updated;
            });
        }

        public override async Task<PaletteList> FetchPaletteList(int startIndex, int count, FilteringSettings filtering)
        {
            if (cachedPalettes == null)
            {
                await RefreshCacheAll();
            }

            PaletteList result = new PaletteList
            {
                Palettes = new WpfObservableRangeCollection<Palette>()
            };

            var filteredPalettes = cachedPalettes.Where(filtering.Filter).ToArray();

            if (startIndex >= filteredPalettes.Length) return result;

            for (int i = 0; i < count; i++)
            {
                if (startIndex + i >= filteredPalettes.Length) break;
                Palette palette = filteredPalettes[startIndex + i];
                result.Palettes.Add(palette);
            }

            result.FetchedCorrectly = true;
            return result;
        }

        public static bool PaletteExists(string paletteName)
        {
            string finalFileName = paletteName;
            if (!paletteName.EndsWith(".pal"))
            {
                finalFileName += ".pal";
            }

            return File.Exists(Path.Join(PathToPalettesFolder, finalFileName));
        }

        public static string GetNonExistingName(string currentName, bool appendExtension = false)
        {
            string newName = Path.GetFileNameWithoutExtension(currentName);

            if (File.Exists(Path.Join(PathToPalettesFolder, newName + ".pal")))
            {
                int number = 1;
                while (true)
                {
                    string potentialName = $"{newName} ({number})";
                    number++;
                    if (File.Exists(Path.Join(PathToPalettesFolder, potentialName + ".pal")))
                        continue;
                    newName = potentialName;
                    break;
                }
            }

            if (appendExtension)
                newName += ".pal";

            return newName;
        }

        public async Task SavePalette(string fileName, SKColor[] colors)
        {
            watcher.EnableRaisingEvents = false;
            string path = Path.Join(PathToPalettesFolder, fileName);
            InitDir();
            await JascFileParser.SaveFile(path, new PaletteFileData(colors));
            watcher.EnableRaisingEvents = true;
            
            await RefreshCache(RefreshType.Created, path);
        }

        public async Task DeletePalette(string name)
        {
            if (!Directory.Exists(PathToPalettesFolder)) return;
            string path = Path.Join(PathToPalettesFolder, name);
            if (!File.Exists(path)) return;

            watcher.EnableRaisingEvents = false;
            File.Delete(path);
            watcher.EnableRaisingEvents = true;
            
            await RefreshCache(RefreshType.Deleted, path);
        }

        public void RenamePalette(string oldFileName, string newFileName)
        {
            if (!Directory.Exists(PathToPalettesFolder)) 
                return;
            
            string oldPath = Path.Join(PathToPalettesFolder, oldFileName);
            string newPath = Path.Join(PathToPalettesFolder, newFileName);
            if (!File.Exists(oldPath) || File.Exists(newPath)) 
                return;

            watcher.EnableRaisingEvents = false;
            File.Move(oldPath, newPath);
            watcher.EnableRaisingEvents = true;

            RefreshCacheRenamed(newPath, oldPath);
        }

        public async Task RefreshCacheAll()
        {
            string[] files = DirectoryExtensions.GetFiles(
                PathToPalettesFolder,
                string.Join("|", AvailableParsers.SelectMany(x => x.SupportedFileExtensions)),
                SearchOption.TopDirectoryOnly);
            cachedPalettes = await ParseAll(files);
            CacheUpdated?.Invoke(RefreshType.All, null, null);
        }

        private async void FileSystemChanged(object sender, FileSystemEventArgs e)
        {
            bool waitableExceptionOccured = false;
            do
            {
                try
                {
                    switch (e.ChangeType)
                    {
                        case WatcherChangeTypes.Created:
                            await RefreshCache(RefreshType.Created, e.FullPath);
                            break;
                        case WatcherChangeTypes.Deleted:
                            await RefreshCache(RefreshType.Deleted, e.FullPath);
                            break;
                        case WatcherChangeTypes.Changed:
                            await RefreshCache(RefreshType.Updated, e.FullPath);
                            break;
                        case WatcherChangeTypes.Renamed:
                            // Handled by method below
                            break;
                        case WatcherChangeTypes.All:
                            await RefreshCache(RefreshType.Created, e.FullPath);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    waitableExceptionOccured = false;
                }
                catch (IOException)
                {
                    waitableExceptionOccured = true;
                    await Task.Delay(100);
                }

            }
            while (waitableExceptionOccured);
        }

        private async Task RefreshCache(RefreshType refreshType, string file)
        {
            Palette updated = null;
            string affectedFileName = null;

            switch (refreshType)
            {
                case RefreshType.All:
                    throw new ArgumentException("To handle refreshing all items, use RefreshCacheAll");
                case RefreshType.Created:
                    updated = await RefreshCacheAdded(file);
                    break;
                case RefreshType.Updated:
                    updated = await RefreshCacheUpdated(file);
                    break;
                case RefreshType.Deleted:
                    affectedFileName = RefreshCacheDeleted(file);
                    break;
                case RefreshType.Renamed:
                    throw new ArgumentException("To handle renaming, use RefreshCacheRenamed");
                default:
                    throw new ArgumentOutOfRangeException(nameof(refreshType), refreshType, null);
            }
            CacheUpdated?.Invoke(refreshType, updated, affectedFileName);
        }

        private void RefreshCacheRenamed(string newFilePath, string oldFilePath)
        {
            string oldFileName = Path.GetFileName(oldFilePath);
            int index = cachedPalettes.FindIndex(p => p.FileName == oldFileName);
            if (index == -1) return;

            Palette palette = cachedPalettes[index];
            palette.FileName = Path.GetFileName(newFilePath);
            palette.Name = Path.GetFileNameWithoutExtension(newFilePath);

            CacheUpdated?.Invoke(RefreshType.Renamed, palette, oldFileName);
        }

        private string RefreshCacheDeleted(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            int index = cachedPalettes.FindIndex(p => p.FileName == fileName);
            if (index == -1) return null;

            cachedPalettes.RemoveAt(index);
            return fileName;
        }

        private async Task<Palette> RefreshCacheItem(string file, Action<Palette> action)
        {
            if (File.Exists(file))
            {
                string extension = Path.GetExtension(file);
                var foundParser = AvailableParsers.FirstOrDefault(x => x.SupportedFileExtensions.Contains(extension));
                if (foundParser != null)
                {
                    var newPalette = await foundParser.Parse(file);
                    if (newPalette is { IsCorrupted: false })
                    {
                        Palette pal = CreatePalette(newPalette, file,
                            cachedFavoritePalettes?.Contains(newPalette.Title) ?? false);
                        action(pal);

                        return pal;
                    }
                }
            }

            return null;
        }

        private async Task<Palette> RefreshCacheUpdated(string file)
        {
            return await RefreshCacheItem(file, palette =>
            {
                Palette existingPalette = cachedPalettes.FirstOrDefault(x => x.FileName == palette.FileName);
                if (existingPalette != null)
                {
                    existingPalette.Colors = palette.Colors.ToList();
                    existingPalette.Name = palette.Name;
                    existingPalette.FileName = palette.FileName;
                }
            });
        }

        private async Task<Palette> RefreshCacheAdded(string file)
        {
            return await RefreshCacheItem(file, palette =>
            {
                string fileName = Path.GetFileName(file);
                int index = cachedPalettes.FindIndex(p => p.FileName == fileName);
                if (index != -1)
                {
                    cachedPalettes.RemoveAt(index);
                }
                cachedPalettes.Add(palette);
            });
        }

        private async Task<List<Palette>> ParseAll(string[] files)
        {
            List<Palette> result = new List<Palette>();

            foreach (var file in files)
            {
                string extension = Path.GetExtension(file);
                if (!File.Exists(file)) continue;
                var foundParser = AvailableParsers.First(x => x.SupportedFileExtensions.Contains(extension));
                {
                    PaletteFileData fileData = await foundParser.Parse(file);
                    if (fileData.IsCorrupted) continue;
                    var palette = CreatePalette(fileData, file, cachedFavoritePalettes?.Contains(fileData.Title) ?? false);

                    result.Add(palette);
                }
            }

            return result;
        }

        private static Palette CreatePalette(PaletteFileData fileData, string file, bool isFavourite)
        {
            var palette = new Palette(
                fileData.Title,
                new List<string>(fileData.GetHexColors()),
                Path.GetFileName(file))
            {
                IsFavourite = isFavourite
            };

            return palette;
        }

        private void RenamedFile(object sender, RenamedEventArgs e)
        {
            RefreshCacheRenamed(e.FullPath, e.OldFullPath);
        }

        private static void InitDir()
        {
            if (!Directory.Exists(PathToPalettesFolder))
            {
                Directory.CreateDirectory(PathToPalettesFolder);
            }
        }
    }
}
