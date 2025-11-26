using System;
using Microsoft.Win32;
using System.Windows.Controls.Primitives;
using System.IO;

namespace SimpleLLMChatGUI
{
    public class ImageHandler
    {
        public bool IsImageAttached { get; private set; }
        public string AttachedImagePath { get; private set; }

        public event Action<string> ImageSelected;
        public event Action ImageDetached;

        public void AttachImageFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;
            AttachedImagePath = path;
            IsImageAttached = true;
            ImageSelected?.Invoke(AttachedImagePath);
        }

        public bool SelectImage()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "Select an image";
            openFileDialog.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif";

            if (openFileDialog.ShowDialog() == true)
            {
                AttachedImagePath = openFileDialog.FileName;
                IsImageAttached = true;
                ImageSelected?.Invoke(AttachedImagePath);
                return true;
            }

            return false;
        }

        public void DetachImage()
        {
            IsImageAttached = false;
            AttachedImagePath = null;
            ImageDetached?.Invoke();
        }
    }
}