using System;
using System.Drawing;

namespace MeesaMultisMaker.Utils
{
    /// <summary>
    /// Helper methods for safe image operations
    /// </summary>
    public static class ImageHelper
    {
        /// <summary>
        /// Checks if an image is valid and can be safely accessed.
        /// Returns false if image is null, disposed, or has invalid dimensions.
        /// </summary>
        public static bool IsValidImage(Image image)
        {
            if (image == null)
                return false;

            try
            {
                // Accessing Width will throw if the image is disposed or invalid
                var _ = image.Width;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a Bitmap is valid and can be safely accessed.
        /// Returns false if bitmap is null, disposed, or has invalid dimensions.
        /// </summary>
        public static bool IsValidBitmap(Bitmap bitmap)
        {
            return IsValidImage(bitmap);
        }

        /// <summary>
        /// Safely gets the width of an image, returning 0 if invalid.
        /// </summary>
        public static int SafeGetWidth(Image image)
        {
            if (!IsValidImage(image))
                return 0;
            
            try
            {
                return image.Width;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Safely gets the height of an image, returning 0 if invalid.
        /// </summary>
        public static int SafeGetHeight(Image image)
        {
            if (!IsValidImage(image))
                return 0;

            try
            {
                return image.Height;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Safely gets the size of an image, returning Size.Empty if invalid.
        /// </summary>
        public static Size SafeGetSize(Image image)
        {
            if (!IsValidImage(image))
                return Size.Empty;

            try
            {
                return image.Size;
            }
            catch
            {
                return Size.Empty;
            }
        }

        /// <summary>
        /// Safely disposes an image if it's not null.
        /// </summary>
        public static void SafeDispose(ref Image image)
        {
            if (image != null)
            {
                try
                {
                    image.Dispose();
                }
                catch { }
                finally
                {
                    image = null;
                }
            }
        }

        /// <summary>
        /// Safely disposes a bitmap if it's not null.
        /// </summary>
        public static void SafeDispose(ref Bitmap bitmap)
        {
            if (bitmap != null)
            {
                try
                {
                    bitmap.Dispose();
                }
                catch { }
                finally
                {
                    bitmap = null;
                }
            }
        }

        /// <summary>
        /// Safely sets an image on a PictureBox, disposing the old image first.
        /// </summary>
        public static void SafeSetPictureBoxImage(System.Windows.Forms.PictureBox pictureBox, Image newImage)
        {
            if (pictureBox == null)
                return;

            var oldImage = pictureBox.Image;
            pictureBox.Image = newImage;

            // Only dispose if the old image is different from the new one
            if (oldImage != null && oldImage != newImage)
            {
                try
                {
                    oldImage.Dispose();
                }
                catch { }
            }
        }

        /// <summary>
        /// Creates a safe copy of an image. Returns null if source is invalid.
        /// </summary>
        public static Bitmap SafeCopy(Image source)
        {
            if (!IsValidImage(source))
                return null;

            try
            {
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }
    }
}
