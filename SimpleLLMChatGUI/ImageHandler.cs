using System;
using Microsoft.Win32;

namespace SimpleLLMChatGUI
{
    public class ImageHandler
    {
        public bool IsImageAttached { get; private set; }
        public string AttachedImagePath { get; private set; }

        public event Action ImageSelected;
        public event Action ImageDetached;

        public bool SelectImage()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "Select an image";
            openFileDialog.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif";

            if (openFileDialog.ShowDialog() == true)
            {
                AttachedImagePath = openFileDialog.FileName;
                IsImageAttached = true;
                ImageSelected?.Invoke();
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