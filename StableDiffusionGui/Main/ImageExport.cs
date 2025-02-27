﻿using StableDiffusionGui.Data;
using StableDiffusionGui.Io;
using StableDiffusionGui.MiscUtils;
using StableDiffusionGui.Ui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StableDiffusionGui.Main
{
    internal class ImageExport
    {
        private static readonly int _maxPathLength = 255;
        private static readonly int _minimumImageAgeMs = 200;
        private static readonly int _loopWaitBeforeStartMs = 1000;
        private static readonly int _loopWaitTimeMs = 200;

        public static async Task ExportLoop(string imagesDir, int startingImgCount, int targetImgCount, bool show)
        {
            TtiTaskInfo currTask = TextToImage.CurrentTask;
            TtiSettings currSettings = TextToImage.CurrentTaskSettings;

            Logger.Log("ExportLoop START", true);
            List<string> outImgs = new List<string>();

            await Task.Delay(_loopWaitBeforeStartMs);

            bool inclPrompt = !currTask.SubfoldersPerPrompt && Config.Get<bool>(Config.Keys.PromptInFilename);
            bool inclSeed = Config.Get<bool>(Config.Keys.SeedInFilename);
            bool inclScale = Config.Get<bool>(Config.Keys.ScaleInFilename);
            bool inclSampler = Config.Get<bool>(Config.Keys.SamplerInFilename);
            bool inclModel = Config.Get<bool>(Config.Keys.ModelInFilename);
            bool sessionDir = Config.Get<bool>(Config.Keys.FolderPerSession);

            while (!TextToImage.Canceled)
            {
                try
                {
                    var files = IoUtils.GetFileInfosSorted(imagesDir, false, "*.png");
                    bool running = TtiProcess.CurrentProcess != null && !TtiProcess.CurrentProcess.HasExited;

                    if (currSettings.Implementation == Enums.StableDiffusion.Implementation.OptimizedSd)
                        running = IoUtils.GetFileInfosSorted(Paths.GetSessionDataPath(), false, "prompts*.*").Any();
                    if (currSettings.Implementation == Enums.StableDiffusion.Implementation.InvokeAi)
                        running = (currTask.ImgCount - startingImgCount) < targetImgCount;

                    if (!running && !files.Any())
                    {
                        Logger.Log($"ExportLoop: Breaking. Process running: {running} - Any files exist: {files.Any()}", true);
                        break;
                    }

                    var images = files.Where(x => x.CreationTime > currTask.StartTime).OrderBy(x => x.CreationTime).ToList(); // Find images and sort by date, newest to oldest
                    images = images.Where(x => !IoUtils.IsFileLocked(x)).ToList(); // Ignore files that are still in use
                    images = images.Where(x => (DateTime.Now - x.LastWriteTime).TotalMilliseconds >= _minimumImageAgeMs).ToList(); // Wait a certain time to make sure python is done writing to it

                    if (currSettings.Implementation == Enums.StableDiffusion.Implementation.InvokeAi)
                    {
                        string log = Logger.GetSessionLog(Constants.Lognames.Sd);
                        images = images.Where(img => log.Contains(img.Name)).ToList(); // Only take image if it was written into SD log. Avoids copying too early (post-proc etc)
                    }

                    Dictionary<string, string> imageDirMap = new Dictionary<string, string>();

                    if (currTask.SubfoldersPerPrompt)
                    {
                        foreach (var img in images)
                        {
                            var imgTimeSinceLastWrite = DateTime.Now - img.LastWriteTime;
                            string prompt = IoUtils.GetImageMetadata(img.FullName).CombinedPrompt;
                            int pathBudget = 255 - img.Directory.FullName.Length - 65;
                            prompt = currTask.IgnoreWildcardsForFilenames && currSettings.ProcessedAndRawPrompts.ContainsKey(prompt) ? currSettings.ProcessedAndRawPrompts[prompt] : prompt;
                            string dirName = string.IsNullOrWhiteSpace(prompt) ? $"unknown_prompt_{FormatUtils.GetUnixTimestamp()}" : FormatUtils.SanitizePromptFilename(FormatUtils.GetPromptWithoutModifiers(prompt), pathBudget);
                            string subDirPath = sessionDir ? Path.Combine(currTask.OutDir, Paths.SessionTimestamp, dirName) : Path.Combine(currTask.OutDir, dirName);
                            imageDirMap[img.FullName] = Directory.CreateDirectory(subDirPath).FullName;
                        }
                    }

                    List<string> renamedImgPaths = new List<string>();

                    for (int i = 0; i < images.Count; i++)
                    {
                        try
                        {
                            var img = images[i];
                            string number = $"{(sessionDir ? "" : "-")}{(currTask.ImgCount).ToString().PadLeft(currTask.TargetImgCount.ToString().Length, '0')}";
                            string parentDir = currTask.SubfoldersPerPrompt ? imageDirMap[img.FullName] : currTask.OutDir;
                            string renamedPath = FormatUtils.GetExportFilename(img.FullName, parentDir, number, "png", _maxPathLength, !sessionDir, inclPrompt, inclSeed, inclScale, inclSampler, inclModel);
                            OverlayMaskIfExists(img.FullName);
                            Logger.Log($"ImageExport: Trying to move {img.Name} => {renamedPath}", true);
                            img.MoveTo(renamedPath);
                            renamedImgPaths.Add(renamedPath);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Failed to move image - Will retry in next loop iteration. ({ex.Message})", true);
                        }
                    }

                    outImgs.AddRange(renamedImgPaths);

                    if (outImgs.Count > 0 && show)
                        Ui.MainForm.ImageViewer.SetImages(outImgs.Where(x => File.Exists(x)).ToList(), Ui.MainForm.ImageViewer.ImgShowMode.ShowLast);

                    await Task.Delay(_loopWaitTimeMs);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Image export error:\n{ex.Message}");
                    Logger.Log($"{ex.StackTrace}", true);
                    break;
                }
            }

            Logger.Log("ExportLoop END", true);
        }

        private static void OverlayMaskIfExists(string imgPath, bool copyMetadata = true)
        {
            if (TextToImage.CurrentTaskSettings.Implementation == Enums.StableDiffusion.Implementation.InvokeAi)
                return; // InvokeAI has proper built-in inpainting - Skip for this implementation

            string maskPath = Inpainting.MaskedImagePath;

            if (!File.Exists(maskPath))
                return;

            ImageMetadata meta = null;

            if (copyMetadata)
                meta = IoUtils.GetImageMetadata(imgPath); // Save metadata as it gets lost otherwise

            ImgUtils.Overlay(imgPath, maskPath); // Actually do the overlaying

            if (meta != null)
                IoUtils.SetImageMetadata(imgPath, meta.ParsedText); // Put metadata back in
        }

    }
}
