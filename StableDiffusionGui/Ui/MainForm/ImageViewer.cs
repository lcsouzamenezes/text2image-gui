﻿using StableDiffusionGui.Data;
using StableDiffusionGui.Io;
using StableDiffusionGui.Main;
using StableDiffusionGui.Os;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StableDiffusionGui.Ui.MainForm
{
    internal class ImageViewer
    {
        public enum ImgShowMode { DontShow, ShowFirst, ShowLast }

        public static string CurrentImagePath { get { try { return _currentImages.Length > 0 ? _currentImages[_currIndex] : ""; } catch { return ""; } } }
        public static ImageMetadata CurrentImageMetadata { get { return IoUtils.GetImageMetadata(CurrentImagePath); } }

        private static string[] _currentImages = new string[0];
        private static int _currIndex = -1;

        public static DateTime TimeOfLastImageViewerInteraction;

        private const string _strNoPrompt = "No prompt to display.";
        private const string _strNoPromptNeg = "No negative prompt to display.";

        /// <returns> The amount of images shown </returns>
        public static int SetImages(string imagesDir, ImgShowMode showMode, int amount = -1, string pattern = "*.png", bool recursive = false)
        {
            var imgPaths = IoUtils.GetFileInfosSorted(imagesDir, recursive, pattern).OrderBy(x => x.CreationTime).Reverse().ToList(); // Find images and sort by date, newest to oldest

            if (amount > 0)
                imgPaths = imgPaths.Take(amount).ToList();

            SetImages(imgPaths.Select(x => x.FullName).ToList(), showMode);

            return imgPaths.Count;
        }

        public static void SetImages(List<string> imagePaths, ImgShowMode showMode, bool ignoreTimeout = false)
        {
            if (Enumerable.SequenceEqual(imagePaths, _currentImages))
                return;

            _currentImages = imagePaths.ToArray();

            if (showMode == ImgShowMode.DontShow)
                return;

            if (ignoreTimeout || (DateTime.Now - TimeOfLastImageViewerInteraction).TotalSeconds >= 3)
            {
                if (showMode == ImgShowMode.ShowFirst)
                    _currIndex = 0;

                if (showMode == ImgShowMode.ShowLast)
                    _currIndex = _currentImages.Length - 1;
            }

            Show();
        }

        public static void AppendImage(string imagePath, ImgShowMode showMode, bool ignoreTimeout = false)
        {
            List<string> newImgList = new List<string>(_currentImages);
            newImgList.Add(imagePath);
            SetImages(newImgList, showMode, ignoreTimeout);
        }

        public static void Show()
        {
            if (_currIndex < 0 || _currIndex >= _currentImages.Length)
            {
                Clear();
                return;
            }

            Program.MainForm.pictBoxImgViewer.Text = "";
            Program.MainForm.pictBoxImgViewer.Image = IoUtils.GetImage(_currentImages[_currIndex]);
            ImagePopup.UpdateSlideshow(Program.MainForm.pictBoxImgViewer.Image);

            ImageMetadata meta = CurrentImageMetadata;

            List<string> infos = new List<string>();

            if (meta.Seed >= 0)
                infos.Add($"Seed {meta.Seed}");

            if (meta.Steps >= 0)
                infos.Add($"{meta.Steps} Steps");

            if (meta.Scale >= 0)
                infos.Add($"Scale {meta.Scale.ToStringDot()}");

            Size res = Program.MainForm.pictBoxImgViewer.Image.Size;

            if (!meta.GeneratedResolution.IsEmpty)
                infos.Add($"{meta.GeneratedResolution.Width}x{meta.GeneratedResolution.Height}{(meta.GeneratedResolution == res ? "" : $" => {res.Width}x{res.Height}")}");

            if (meta.InitStrength > 0.0001f)
                infos.Add($"Strength {meta.InitStrength.ToStringDot()}");

            if (!string.IsNullOrWhiteSpace(meta.Sampler))
                infos.Add(Strings.Samplers.Get(meta.Sampler, true, true));

            Program.MainForm.labelImgInfo.Text = $"Image {_currIndex + 1}/{_currentImages.Length} {(infos.Count > 0 ? $" - {string.Join(" - ", infos)}" : "")}";
            Program.MainForm.labelImgPrompt.Text = !string.IsNullOrWhiteSpace(meta.Prompt) ? meta.Prompt : _strNoPrompt;
            Program.MainForm.labelImgPromptNeg.Text = !string.IsNullOrWhiteSpace(meta.NegativePrompt) ? meta.NegativePrompt : _strNoPromptNeg;
            Program.MainForm.toolTip.SetToolTip(Program.MainForm.labelImgPrompt, $"{Program.MainForm.labelImgPrompt.Text}\n\nClick to copy.");
            Program.MainForm.toolTip.SetToolTip(Program.MainForm.labelImgPromptNeg, $"{Program.MainForm.labelImgPromptNeg.Text}\n\nClick to copy.");
            UpdatePromptLabelColors();
        }

        public static void Clear()
        {
            Program.MainForm.pictBoxImgViewer.Text = "";
            Program.MainForm.pictBoxImgViewer.Image = null;
            Program.MainForm.labelImgInfo.Text = "No images to show.";
            Program.MainForm.labelImgPrompt.Text = _strNoPrompt;
            Program.MainForm.labelImgPromptNeg.Text = _strNoPromptNeg;
            UpdatePromptLabelColors();
        }

        public static void UpdatePromptLabelColors ()
        {
            Program.MainForm.labelImgPrompt.ForeColor = Program.MainForm.labelImgPrompt.Text == _strNoPrompt ? Color.Silver : Color.PaleGreen;
            Program.MainForm.labelImgPromptNeg.ForeColor = Program.MainForm.labelImgPromptNeg.Text == _strNoPromptNeg ? Color.Silver : Color.LightCoral;
        }

        public static void Move(bool previous = false)
        {
            TimeOfLastImageViewerInteraction = DateTime.Now;

            if (!previous)
            {
                _currIndex += 1;

                if (_currIndex >= _currentImages.Length)
                    _currIndex = 0;
            }
            else
            {
                _currIndex -= 1;

                if (_currIndex < 0)
                    _currIndex = _currentImages.Length - 1;
            }

            Show();
        }

        public static void CopyCurrentToFavs()
        {
            string dir = Directory.CreateDirectory(Config.Get<string>(Config.Keys.FavsPath)).FullName;

            if (File.Exists(CurrentImagePath))
            {
                string targetPath = Path.Combine(dir, Path.GetFileName(CurrentImagePath));

                try
                {
                    File.Copy(CurrentImagePath, targetPath, true);
                    OsUtils.ShowNotification("Stable Diffusion GUI", $"Copied image to favorites.", false, 1.5f);
                }
                catch(Exception ex)
                {
                    Logger.Log($"Failed to copy image to favorites: {ex.Message}");
                    Logger.Log($"Failed to copy '{CurrentImagePath}' => '{targetPath}'\n{ex.StackTrace}", true);
                }
            }
        }

        public static void OpenCurrent()
        {
            Process.Start(CurrentImagePath);
        }

        public static void OpenFolderOfCurrent()
        {
            Process.Start("explorer", $@"/select, {CurrentImagePath.Wrap()}");
        }

        public static void DeleteCurrent()
        {
            IoUtils.TryDeleteIfExists(CurrentImagePath);
            _currentImages = _currentImages.Where(x => File.Exists(x)).ToArray();
            Move(true);
        }

        public static void DeleteAll(bool askForConfirmation = true)
        {
            if (_currentImages == null || _currentImages.Length < 1)
                return;

            if (askForConfirmation)
            {
                DialogResult dialogResult = UiUtils.ShowMessageBox($"Are you sure you want to delete {_currentImages.Length} generated images?", "Are you sure?", MessageBoxButtons.YesNo);

                if (dialogResult != DialogResult.Yes)
                    return;
            }

            var parentDirs = _currentImages.Select(x => x.GetParentDirOfFile());

            _currentImages.ToList().ForEach(x => IoUtils.TryDeleteIfExists(x));
            _currentImages = _currentImages.Where(x => File.Exists(x)).ToArray();

            if (_currentImages.Length > 0)
                Move(true);
            else
                Clear();

            parentDirs.Where(dir => !Directory.EnumerateFileSystemEntries(dir).Any()).ToList().ForEach(dir => IoUtils.TryDeleteIfExists(dir)); // Delete dir if it's now empty
        }
    }
}
