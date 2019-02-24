using CustomVision;
using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.AI.MachineLearning;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using ZXing;
using ZXing.Common;
using ZXing.Multi.QrCode;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace PostProcess
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        ObjectDetection detection = new ObjectDetection(
            new List<string>(new string[] { "1", "2" }),
                20,
                0.4f
            );
        Dictionary<string, string> finalResult = new Dictionary<string, string>();
        StorageFolder folder = null;
        string zone = "LD";

        public MainPage()
        {
            this.InitializeComponent();
            Task.Run(async () =>
            {
                await setupAi();
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () => {
                    await ProcessDirectory();
                    CoreApplication.Exit();
                });
            });
        }

        async Task ProcessDirectory()
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.FileTypeFilter.Add("*");
            folder = await folderPicker.PickSingleFolderAsync();

            if (folder != null)
            {
                Windows.Storage.AccessCache.StorageApplicationPermissions.
                FutureAccessList.AddOrReplace("PickedFolderToken", folder);
                foreach (StorageFile file in await folder.GetFilesAsync())
                {
                    if (file.FileType.Equals(".csv"))
                    {
                        continue;
                    }

                    System.Diagnostics.Debug.WriteLine("file name: " + file.Name);
                    using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read))
                    {
                        // Create the decoder from the stream 
                        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

                        // Get the SoftwareBitmap representation of the file in BGRA8 format
                        SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();
                        await processSoftwareBitmap(softwareBitmap);
                        //await batchDetect(softwareBitmap);
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("cancel");
            }

            await genCsv();
        }

        async Task setupAi()
        {
            var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///model.onnx"));
            await detection.Init(file);
        }

        async Task genCsv()
        {
            int aisleNum = 2;
            int bayNum = 3;
            int lvNum = 3;
            int colNum = 5;

            for (int aisleIdx = 1; aisleIdx <= aisleNum; aisleIdx++)
            {
                for (int bayIdx = 1; bayIdx <= bayNum; bayIdx++)
                {
                    for (int lvIdx = 1; lvIdx <= lvNum; lvIdx++)
                    {
                        for (int colIdx = 1; colIdx <= colNum; colIdx++)
                        {
                            int pos = (bayIdx-1)*colNum+colIdx;
                            string posId = string.Format("{0}{1}0{2}{3}{4}", zone, aisleIdx, bayIdx, lvIdx, pos.ToString().PadLeft(2, '0'));
                            if (!finalResult.ContainsKey(posId))
                            {
                                finalResult.Add(posId, "");
                            }
                        }
                    }
                }
            }
            Windows.Storage.StorageFile resultFile = await folder.CreateFileAsync("hack2019-7.csv", Windows.Storage.CreationCollisionOption.ReplaceExisting);
            StringBuilder csvbuilder = new StringBuilder();
            foreach (KeyValuePair<string, string> entry in finalResult)
            {
                csvbuilder.AppendLine(string.Format("{0},{1}", entry.Key, entry.Value));
            }
            await Windows.Storage.FileIO.WriteTextAsync(resultFile, csvbuilder.ToString());
        }

        //async Task batchDetect(SoftwareBitmap bitmap)
        //{
        //    var reader = new QRCodeMultiReader();
        //    for (int rowIdx = 0; rowIdx < 3; rowIdx++)
        //    {
        //        for (int colIdx = 0; colIdx < 5; colIdx++)
        //        {
        //            SoftwareBitmap cropedImage = await cropImage(bitmap, colIdx / 5.0f, rowIdx / 3.0f, 1 / 5.0f, 1 / 3.0f);
        //            var source = new SoftwareBitmapLuminanceSource(cropedImage);
        //            HybridBinarizer binarizer = new HybridBinarizer(source);
        //            var results = reader.decodeMultiple(new BinaryBitmap(binarizer));
        //            System.Diagnostics.Debug.WriteLine(rowIdx + ", " + colIdx);
        //            var info = parseZXingResult(results);
        //            finalResult.Add(info["locText"], info["boxText"]);
        //        }

        //    }
        //}

        async Task<SoftwareBitmap> cropImage(SoftwareBitmap bitmap, float startX, float startY, float width, float height)
        {
            return await cropImageWithPixel(bitmap,
                Convert.ToUInt32(bitmap.PixelWidth * startX),
                Convert.ToUInt32(bitmap.PixelHeight * startY),
                Convert.ToUInt32(bitmap.PixelWidth * width) - 1,
                Convert.ToUInt32(bitmap.PixelHeight * height) - 1);
        }

        async Task<SoftwareBitmap> cropImageWithPixel(SoftwareBitmap bitmap, uint startX, uint startY, uint width, uint height)
        {
            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, stream);
                encoder.SetSoftwareBitmap(bitmap);
                encoder.BitmapTransform.Bounds = new BitmapBounds()
                {
                    X = startX,
                    Y = startY,
                    Height = Convert.ToUInt32(Math.Min(height, bitmap.PixelHeight-startY)),
                    Width = Convert.ToUInt32(Math.Min(width, bitmap.PixelWidth-startX))
                };
                await encoder.FlushAsync();

                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }
        }

        Dictionary<string, string> parseZXingResult(ZXing.Result[] results)
        {
            Dictionary<string, string> qrcodeResult = new Dictionary<string, string>();
            string locText = "";
            string boxText = "";

            if (results != null && results.Length > 0)
            {
                for (int resultIdx = 0; resultIdx < results.Length; resultIdx++)
                {
                    ZXing.Result info = results[resultIdx];
                    if (info.BarcodeFormat != BarcodeFormat.QR_CODE)
                    {
                        continue;
                    }

                    if (info.Text.StartsWith('0'))
                    {
                        boxText = info.Text;
                    }
                    else
                    {
                        locText = info.Text;
                    }
                }
                System.Diagnostics.Debug.WriteLine("locText: " + locText);
                System.Diagnostics.Debug.WriteLine("boxText: " + boxText);
            }
            qrcodeResult.Add("locText", locText);
            qrcodeResult.Add("boxText", boxText);
            return qrcodeResult;
        }

        async Task<SoftwareBitmap> resizeImgForDetect(SoftwareBitmap bitmap)
        {
            uint destWidthAndHeight = 416;
            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, stream);
                encoder.SetSoftwareBitmap(bitmap);
                encoder.BitmapTransform.ScaledWidth = destWidthAndHeight;
                encoder.BitmapTransform.ScaledHeight = destWidthAndHeight;
                encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Linear;
                await encoder.FlushAsync();

                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }
        }

        async Task saveDebugImage(SoftwareBitmap bitmap, IList<PredictionModel> result)
        {
            WriteableBitmap newImage = new WriteableBitmap(bitmap.PixelWidth, bitmap.PixelHeight);
            bitmap.CopyToBuffer(newImage.PixelBuffer);
            for (int predictIdx = 0; predictIdx < result.Count; predictIdx++)
            {
                PredictionModel predictInfo = result[predictIdx];
                newImage.DrawRectangle((int)predictInfo.BoundingBox.Left*bitmap.PixelWidth,
                    (int)predictInfo.BoundingBox.Top*bitmap.PixelHeight, 
                    (int)predictInfo.BoundingBox.Width*bitmap.PixelWidth, 
                    (int)predictInfo.BoundingBox.Height*bitmap.PixelHeight,
                    Colors.Red);
            }

            Windows.Storage.StorageFile testFile = await folder.CreateFileAsync("test.jpg", Windows.Storage.CreationCollisionOption.ReplaceExisting);
            using (IRandomAccessStream stream = await testFile.OpenAsync(FileAccessMode.ReadWrite))
            {
                Guid BitmapEncoderGuid = BitmapEncoder.JpegEncoderId;
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoderGuid, stream);
                Stream pixelStream = newImage.PixelBuffer.AsStream();
                byte[] pixels = new byte[pixelStream.Length];
                await pixelStream.ReadAsync(pixels, 0, pixels.Length);

                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                                    (uint)newImage.PixelWidth,
                                    (uint)newImage.PixelHeight,
                                    96.0,
                                    96.0,
                                    pixels);
                await encoder.FlushAsync();
            }

        }

        public async Task<IList<IDictionary<string, float>>> processSoftwareBitmap(SoftwareBitmap bitmap)
        {
            SoftwareBitmap bitmapToProcess = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            uint cropSize = Convert.ToUInt32(bitmap.PixelHeight);
            uint sideDiff = Convert.ToUInt32(bitmap.PixelWidth - bitmap.PixelHeight);
            SoftwareBitmap refCropedImage = await cropImageWithPixel(bitmap,
                sideDiff,
                0,
                cropSize,
                cropSize);
            bitmapToProcess = await resizeImgForDetect(refCropedImage);
            
            //Windows.Storage.StorageFile testFile = await folder.CreateFileAsync("test.jpg", Windows.Storage.CreationCollisionOption.ReplaceExisting);
            //using (IRandomAccessStream stream = await testFile.OpenAsync(FileAccessMode.ReadWrite))
            //{
            //    Guid BitmapEncoderGuid = BitmapEncoder.JpegEncoderId;
            //    BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoderGuid, stream);
            //    encoder.SetSoftwareBitmap(refCropedImage);
            //    await encoder.FlushAsync();
            //}

            //Convert SoftwareBitmap  into VideoFrame
            using (VideoFrame frame = VideoFrame.CreateWithSoftwareBitmap(bitmapToProcess))
            {
                if (frame == null)
                {
                    return null;
                }

                try
                {
                    modelInput input = new modelInput();
                    input.data = ImageFeatureValue.CreateFromVideoFrame(frame);
                    if (input.data == null)
                    {
                        return null;
                    }
                    
                    IList<PredictionModel> result = await detection.PredictImageAsync(frame);
                    //await saveDebugImage(refCropedImage, result);
                    var reader = new QRCodeMultiReader();
                    for (int predictIdx=0; predictIdx < result.Count; predictIdx++)
                    {
                        PredictionModel predictInfo = result[predictIdx];
                        SoftwareBitmap cropedImage = await cropImage(refCropedImage, 
                            Math.Max(predictInfo.BoundingBox.Left, 0),
                            Math.Max(predictInfo.BoundingBox.Top, 0),
                            predictInfo.BoundingBox.Width,
                            predictInfo.BoundingBox.Height);

                        //Windows.Storage.StorageFile testFile = await folder.CreateFileAsync("test" + predictIdx + ".jpg", Windows.Storage.CreationCollisionOption.ReplaceExisting);
                        //using (IRandomAccessStream stream = await testFile.OpenAsync(FileAccessMode.ReadWrite))
                        //{
                        //    Guid BitmapEncoderGuid = BitmapEncoder.JpegEncoderId;
                        //    BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoderGuid, stream);
                        //    encoder.SetSoftwareBitmap(cropedImage);
                        //    await encoder.FlushAsync();
                        //}

                        var source = new SoftwareBitmapLuminanceSource(cropedImage);
                        HybridBinarizer binarizer = new HybridBinarizer(source);
                        var results = reader.decodeMultiple(new BinaryBitmap(binarizer));
                        var info = parseZXingResult(results);
                        string locText = info["locText"];
                        string boxText = info["boxText"];
                        if (locText.Equals("") || !locText.StartsWith(zone))
                        {
                            continue;
                        }

                        if (finalResult.ContainsKey(locText))
                        {
                            if (finalResult[locText].Equals(""))
                            {
                                finalResult[locText] = boxText;
                            }
                        } else
                        {
                            finalResult.Add(locText, boxText);
                        }
                    }
                }
                catch (InvalidOperationException ie)
                {
                    return null;
                }
                catch (Exception e)
                {
                    return null;
                }
                finally
                {
                    bitmap.Dispose();

                }


            }

            return null;
        }
    }
}
