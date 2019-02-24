using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using DJI.WindowsSDK;
using DJI.WindowsSDK.Components; 
using Windows.UI.Popups;
using Windows.UI.Xaml.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Multi.QrCode;
using Windows.Graphics.Imaging;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace WSDKTest
{
    public sealed partial class MainPage : Page
    {
        private DJIVideoParser.Parser videoParser;
        public WriteableBitmap VideoSource;

        //Worker task (thread) for reading barcode
        //As reading barcode is computationally expensive
        private Task readerWorker = null;
        private ISet<string> readed = new HashSet<string>();

        private object bufLock = new object();
        //these properties are guarded by bufLock
        private int width, height;
        private byte[] decodedDataBuf;

        public MainPage()
        {
            this.InitializeComponent();
            //Listen for registration success
            DJISDKManager.Instance.SDKRegistrationStateChanged += async (state, result) =>
            {
                if (state != SDKRegistrationState.Succeeded)
                {
                    var md = new MessageDialog(result.ToString());
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () => await md.ShowAsync());
                    return;
                }
                //wait till initialization finish
                //use a large enough time and hope for the best
                await Task.Delay(1000);

                /*
                VideoResolutionAndFrameRate higher = new VideoResolutionAndFrameRate()
                {
                    resolution = VideoResolution.RESOLUTION_640x512,
                    frameRate = VideoFrameRate.RATE_24FPS
                };
                await DJISDKManager.Instance.ComponentManager.GetCameraHandler(0, 0).SetVideoResolutionAndFrameRateAsync(higher);
                */

                videoParser = new DJIVideoParser.Parser();
                videoParser.Initialize();
                videoParser.SetVideoDataCallack(0, 0, ReceiveDecodedData);
                DJISDKManager.Instance.VideoFeeder.GetPrimaryVideoFeed(0).VideoDataUpdated += OnVideoPush;

                await DJISDKManager.Instance.ComponentManager.GetFlightAssistantHandler(0, 0).SetObstacleAvoidanceEnabledAsync(new BoolMsg() { value = false });


                await Task.Delay(5000);
                GimbalResetCommandMsg resetMsg = new GimbalResetCommandMsg() { value = GimbalResetCommand.UNKNOWN };

                await DJISDKManager.Instance.ComponentManager.GetGimbalHandler(0, 0).ResetGimbalAsync(resetMsg);

                var res = await DJISDKManager.Instance.ComponentManager.GetCameraHandler(0, 0).SetShootPhotoModeAsync(new CameraShootPhotoModeMsg() { value = CameraShootPhotoMode.NORMAL });
                System.Diagnostics.Debug.WriteLine("Set Camera Mode Debug " + res.ToString());


            };
           
            DJISDKManager.Instance.RegisterApp("c6e6f85869ddffbac6264032");
            Text1.Text = "Initialisation Complete. ";
        }

        void OnVideoPush(VideoFeed sender, [ReadOnlyArray] ref byte[] bytes)
        {
            videoParser.PushVideoData(0, 0, bytes, bytes.Length);
        }

        async Task startPath()
        {
            await Task.Delay(1500);
            await rollDrone(0.3f, 200);
            await Task.Delay(1500);
            await throttleDrone(0.3f, 2000);
            await Task.Delay(1500);
            await pitchDrone(0.3f, 2000);
            await Task.Delay(1500);
            await rollDrone(-0.3f, 2000);
            await Task.Delay(1500);
            await pitchDrone(-0.3f, 2000);
            await Task.Delay(1500);
            await throttleDrone(-0.1f, 1500);
        }

        async Task start12Carton()
        {
            int movecount = 2;

            int pitchint = 100;
            int yaw90 = 1350;
            int move1 = 500;
            int move2 = 1400;
            int down1 = 1000;
            await Task.Delay(100);
            // Take off
            var res = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartTakeoffAsync();
            System.Diagnostics.Debug.WriteLine(res.ToString());
            await Task.Delay(6000);
            // Get into pos while Taking off
            //await throttleDrone(-1.0f, 500);
            //await Task.Delay(1500);
            await rollDrone(-1.0f, move1);
            await Task.Delay(1500);
            for (int i = movecount; i > 0; i--)
            {
                await rollDrone(-1.0f, move2);
                await Task.Delay(1500);
            }
            await pitchGimbal(-1.0f, 500);
            await Task.Delay(100);
            await pitchGimbal(-1.0f, 500);
            await Task.Delay(100);
            await TakeAPic();
            await Task.Delay(500); 
            await throttleDrone(-1.0f, 1000);
            await Task.Delay(1500); 
            
            for (int i = movecount; i > 0; i--)
            {
                await rollDrone(1.0f, move2);
                await Task.Delay(1500);
            }
            // cuz im too lazy pls fly back
            /*await rollDrone(1.0f, move2);
            await Task.Delay(1000);
            await rollDrone(1.0f, move2);
            await Task.Delay(1000);*/

            // Turn around and do it again
            await yawDrone(1.0f, 2595);
            await Task.Delay(1500);
            //await pitchDrone(1.0f, 300);
            //await Task.Delay(1500); 
            for (int i=movecount; i>0; i--)
            {
                await rollDrone(1.0f, move2);
                await Task.Delay(1500); 
            }
            await throttleDrone(0.3f, down1);
            await Task.Delay(1500);
            await pitchGimbal(1.0f, 500);
            await Task.Delay(100);
            await pitchGimbal(1.0f, 500);
            await Task.Delay(100);
            await TakeAPic();
            await Task.Delay(500);
            for (int i=movecount; i>0; i--)
            {
                await rollDrone(-1.0f, move2);
                await Task.Delay(1500); 
            }
            // Landing, need to Move up and Align Centre
            res = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartAutoLandingAsync();
            await Task.Delay(5000);
            Text1.Text = "Mission Complete. "; 
        }
        
        async Task startbrakecheck()
        {
            int movecount = 1;

            int pitchint = 1100; // New Battery Debug start 900, too large will crash
            int rollint = 1350;
            int throttleint = 1000;
            int pitchmid = 2100; // New Battery Debug start ___, too large will crash
            int rollmid = 300; 
            int yaw90 = 1297;
            int move1 = 800;
            int move2 = 1400;
            int move4 = 2500; 
            int down1 = 1400;
            int pausetime = 1500;
            int stoplandingtime = 3000; 
            await Task.Delay(100);
            Text1.Text = "Commence 15 Box Take Off Mission"; 
            // Take off
            var res = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartTakeoffAsync();
            System.Diagnostics.Debug.WriteLine("15 Box Take Off Debug " + res.ToString());
            await Task.Delay(6000);
            // Get into pos, take a pic
            await rollDrone(-1.0f, rollint); 
            await Task.Delay(pausetime);
            // await throttleDrone(-1.0f, throttleint);
            await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartAutoLandingAsync();
            await Task.Delay(stoplandingtime);
            await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StopAutoLandingAsync(); 
            await Task.Delay(pausetime);
            await pitchDrone(-0.8f, pitchint);
            await Task.Delay(pausetime*2);
            await TakeAPic();
            await Task.Delay(pausetime);
            
            // Repeat moves
            for (int i=movecount; i>0; i--)
            {
                // await rollDrone(-1.0f, move4);
                // await Task.Delay(pausetime); 
                // await TakeAPic(); 
                // await Task.Delay(pausetime); 
            }
            // Turn Around and Get into another Ready Position, Take a Pic
            await yawDrone(1.0f, yaw90 * 2);
            await Task.Delay(pausetime);
            await rollDrone(-1.0f, rollmid);
            await Task.Delay(pausetime); 
            await pitchDrone(-0.8f, pitchmid);
            await Task.Delay(pausetime*2);
            await TakeAPic(); 
            await Task.Delay(pausetime); 
            // Repeat moves
            for (int i = movecount; i > 0; i--)
            {
                // await rollDrone(-1.0f, move4);
                // await Task.Delay(pausetime); 
                // await TakeAPic(); 
                // await Task.Delay(pausetime); 
            }

            // Landing, need move up and align centre
            await pitchDrone(0.8f, pitchint);
            await Task.Delay(pausetime);
            await throttleDrone(0.3f, throttleint);
            await Task.Delay(pausetime);
            res = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartAutoLandingAsync();
            System.Diagnostics.Debug.WriteLine("15 Box Landing Debug " + res.ToString());
            await Task.Delay(6000); 
            Text1.Text = "Mission Complete. "; 
        }

        async Task start6pic()
        {
            int movecount = 1;

            int pitchint = 1000; // New Battery Debug start 900, too large will crash
            int rollint = 1350;
            int throttleint = 1000;
            int pitchmid = 2100; // New Battery Debug start ___, too large will crash
            int rollmid = 300;
            int yaw90 = 1297;
            int move1 = 800;
            int move2 = 1400;
            int move4 = 2500;
            int down1 = 1400;
            int pausetime = 1500;
            int stoplandingtime = 3000;
            await Task.Delay(100);
            Text1.Text = "Commence 15 Box 6 pic Mission";
            // Take off
            var res = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartTakeoffAsync();
            System.Diagnostics.Debug.WriteLine("15 Box Take Off Debug " + res.ToString());
            await Task.Delay(6000);
            await rollDrone(-1.0f, rollint);
            await Task.Delay(pausetime);
            // await throttleDrone(-1.0f, throttleint);
            await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartAutoLandingAsync();
            await Task.Delay(stoplandingtime);
            await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StopAutoLandingAsync();
            await Task.Delay(pausetime);
            await pitchDrone(-0.8f, pitchint);
            await Task.Delay(pausetime * 2);
            await TakeAPic();
            await Task.Delay(pausetime);
            // complete P1

            await rollDrone(-1.0f, move4);
            await Task.Delay(pausetime * 2);
            await TakeAPic();
            await Task.Delay(pausetime);
            //complete P2

            await rollDrone(-1.0f, move4);
            await Task.Delay(pausetime * 2);
            await TakeAPic();
            await Task.Delay(pausetime);
            //complete P3

            // Turn Around and Get into P4, Take a Pic
            await yawDrone(1.0f, yaw90 * 2);
            await Task.Delay(pausetime);
            //await rollDrone(-1.0f, rollmid);
            //await Task.Delay(pausetime);
            await pitchDrone(-0.8f, pitchmid);
            await Task.Delay(pausetime * 2);
            await TakeAPic();
            await Task.Delay(pausetime);
            //complete P4

            await rollDrone(-1.0f, move4);
            await Task.Delay(pausetime * 2);
            await TakeAPic();
            await Task.Delay(pausetime);
            //complete P5

            await rollDrone(-1.0f, move4);
            await Task.Delay(pausetime * 2);
            await TakeAPic();
            await Task.Delay(pausetime);
            //complete P6


            // Landing, need move up and align centre
            await pitchDrone(0.8f, pitchint);
            await Task.Delay(pausetime);
            await throttleDrone(0.3f, throttleint);
            await Task.Delay(pausetime);
            res = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartAutoLandingAsync();
            System.Diagnostics.Debug.WriteLine("15 Box Landing Debug " + res.ToString());
            await Task.Delay(6000);
            Text1.Text = "Mission Complete. ";
        }

        async Task startOverlapColumn()
        {
            int movecount = 4;

            int pitchint = 1000; // New Battery Debug start 900, too large will crash
            int rollint = 1000;
            int throttleint = 1000;
            int pitchmid = 1900; // New Battery Debug start ___, too large will crash
            int rollmid = 500;
            int yaw90 = 1297;
            int move1 = 800;
            int move2 = 1400;
            int move4 = 1700;
            int down1 = 1400;
            int pausetime = 1500;
            int stoplandingtime = 3100;
            await Task.Delay(100);
            Text1.Text = "Commence Overlap Column Mission";
            // Take off
            var res = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartTakeoffAsync();
            System.Diagnostics.Debug.WriteLine("Overlap Take Off Debug " + res.ToString());
            await Task.Delay(6000);
            // Get into pos, take a pic
            await rollDrone(-1.0f, rollint);
            await Task.Delay(pausetime);
            // await throttleDrone(-1.0f, throttleint);
            await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartAutoLandingAsync();
            await Task.Delay(stoplandingtime);
            await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StopAutoLandingAsync();
            await Task.Delay(pausetime);
            await pitchDrone(-0.8f, pitchint);
            await Task.Delay(pausetime * 2);
            await TakeAPic();
            await Task.Delay(pausetime);

            // Repeat moves
            for (int i = movecount; i > 0; i--)
            {
                await rollDrone(-1.0f, move4);
                await Task.Delay(pausetime); 
                await TakeAPic(); 
                await Task.Delay(pausetime); 
            }
            // Rack Final Pic, need yaw 30 deg
            await yawDrone(-1.0f, yaw90 / 3);
            await Task.Delay(pausetime);
            await TakeAPic();
            await Task.Delay(pausetime);
            await yawDrone(1.0f, yaw90 / 3);
            await Task.Delay(pausetime);
            // Turn Around and Compensate Offset, Take a Pic
            await yawDrone(1.0f, yaw90 * 2);
            await Task.Delay(pausetime);
            await rollDrone(-1.0f, rollmid); // Roll Mid Offset
            await Task.Delay(pausetime);
            await pitchDrone(-0.8f, pitchmid); // Pitch Mid Offset
            await Task.Delay(pausetime * 2);
            await TakeAPic();
            await Task.Delay(pausetime);
            // Repeat moves
            for (int i = movecount; i > 0; i--)
            {
                await rollDrone(-1.0f, move4);
                await Task.Delay(pausetime); 
                await TakeAPic(); 
                await Task.Delay(pausetime); 
            }
            // Rack Final Pic, need yaw 30 deg
            await yawDrone(-1.0f, yaw90 / 3);
            await Task.Delay(pausetime);
            await TakeAPic();
            await Task.Delay(pausetime);
            await yawDrone(1.0f, yaw90 / 3);
            await Task.Delay(pausetime); 


            // Landing, need move up and align centre
            await pitchDrone(0.8f, pitchint);
            await Task.Delay(pausetime);
            await throttleDrone(0.3f, throttleint);
            await Task.Delay(pausetime);
            await rollDrone(-0.7f, rollint);
            await Task.Delay(pausetime); 
            res = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartAutoLandingAsync();
            System.Diagnostics.Debug.WriteLine("Overlap Column Landing Debug " + res.ToString());
            await Task.Delay(6000);
            Text1.Text = "Mission Complete. ";
        }

        async Task TakeAPic()
        {
            Text2.Text = "Attempting To Shoot A Photo to SD Storage. ";
            var res = DJISDKManager.Instance.ComponentManager.GetCameraHandler(0, 0).StartShootPhotoAsync();
            System.Diagnostics.Debug.WriteLine("Take A Photo Debug " + res.ToString()); 
        }

        async Task throttleDrone(float speed, int opTime)
        {
            throttle = speed;
            Text2.Text = "Attempting Ascent (Decend) at speed " + (speed * 100).ToString() + " % for " + (opTime / 1000.0).ToString() + " s. "; 
            try
            {
                if (DJISDKManager.Instance != null)
                {
                    DJISDKManager.Instance.VirtualRemoteController.UpdateJoystickValue(throttle, yaw, pitch, roll);
                    await Task.Delay(opTime);
                    throttle = 0;
                    DJISDKManager.Instance.VirtualRemoteController.UpdateJoystickValue(throttle, yaw, pitch, roll);
                }
            }
            catch (Exception err)
            {
                System.Diagnostics.Debug.WriteLine(err);
            }
        }

        async Task rollDrone(float speed, int opTime)
        {
            roll = speed;
            Text2.Text = "Attempting Rudding Right (Left) at speed " + (speed * 100).ToString() + " % for " + (opTime / 1000.0).ToString() + " s. ";
            try
            {
                if (DJISDKManager.Instance != null)
                {
                    DJISDKManager.Instance.VirtualRemoteController.UpdateJoystickValue(throttle, yaw, pitch, roll);
                    await Task.Delay(opTime);
                    roll = 0;
                    DJISDKManager.Instance.VirtualRemoteController.UpdateJoystickValue(throttle, yaw, pitch, roll);
                }
            }
            catch (Exception err)
            {
                System.Diagnostics.Debug.WriteLine(err);
            }
        }

        async Task pitchDrone(float speed, int opTime)
        {
            pitch = speed;
            Text2.Text = "Attempting Pitching Forward (Backward) at speed " + (speed * 100).ToString() + " % for " + (opTime / 1000.0).ToString() + " s. ";
            try
            {
                if (DJISDKManager.Instance != null)
                {
                    DJISDKManager.Instance.VirtualRemoteController.UpdateJoystickValue(throttle, yaw, pitch, roll);
                    await Task.Delay(opTime);
                    pitch = 0;
                    DJISDKManager.Instance.VirtualRemoteController.UpdateJoystickValue(throttle, yaw, pitch, roll);
                }
            }
            catch (Exception err)
            {
                System.Diagnostics.Debug.WriteLine(err);
            }
        }

        async Task yawDrone (float speed, int opTime)
        {
            yaw = speed;
            Text2.Text = "Attempting Rotating Clockwise (CCW) at speed " + (speed * 100).ToString() + " % for " + (opTime / 1000.0).ToString() + " s. ";
            try
            {
                if (DJISDKManager.Instance != null)
                {
                    DJISDKManager.Instance.VirtualRemoteController.UpdateJoystickValue(throttle, yaw, pitch, roll);
                    await Task.Delay(opTime);
                    yaw = 0;
                    DJISDKManager.Instance.VirtualRemoteController.UpdateJoystickValue(throttle, yaw, pitch, roll);
                }
            }
            catch (Exception err)
            {
                System.Diagnostics.Debug.WriteLine(err);
            }
        }

        async Task pitchGimbal(float speed, int opTime) // bug bug dei
        {
            GimbalAngleRotation rotation = new GimbalAngleRotation()
                {
                    mode = GimbalAngleRotationMode.RELATIVE_ANGLE,
                    pitch = 45,
                    roll = 45,
                    yaw = 45,
                    pitchIgnored = false,
                    yawIgnored = false,
                    rollIgnored = false,
                    duration = speed / 1000.0
                };
                    

            System.Diagnostics.Debug.Write("pitch = 45\n");
            // Defined somewhere else
            var gimbalHandler = DJISDKManager.Instance.ComponentManager.GetGimbalHandler(0, 0);
            System.Diagnostics.Debug.WriteLine(gimbalHandler.ToString()); 

            //Speed
            var gimbalRotation_speed = new GimbalSpeedRotation();
            gimbalRotation_speed.pitch = speed * 10.0;
            var watch = System.Diagnostics.Stopwatch.StartNew(); 
            
            var res = DJISDKManager.Instance.ComponentManager.GetGimbalHandler(0, 0).RotateBySpeedAsync(gimbalRotation_speed);
            var timed = Task.Delay(500);
            var tasks = new Task[] { res, timed };
            await Task.WhenAny(tasks);
            
            watch.Stop();
            int elapsed = (int)watch.ElapsedMilliseconds;
            System.Diagnostics.Debug.WriteLine(elapsed.ToString()); 
            System.Diagnostics.Debug.WriteLine(res.ToString()); 

            //await DJISDKManager.Instance.ComponentManager.GetGimbalHandler(0, 0).RotateByAngleAsync(rotation); 
            //break;
                
            
            /*catch (Exception err)
            {
                System.Diagnostics.Debug.WriteLine(err);
            }*/
        }
        

        void createWorker()
        {
            //create worker thread for reading barcode
            readerWorker = new Task(async () =>
            {
                //use stopwatch to time the execution, and execute the reading process repeatedly
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var reader = new QRCodeMultiReader();
                SoftwareBitmap bitmap;
                HybridBinarizer binarizer;
                while (true)
                {
                    try
                    {
                        lock (bufLock)
                        {
                            bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height);
                            bitmap.CopyFromBuffer(decodedDataBuf.AsBuffer());
                        }
                    }
                    catch
                    {
                        //the size maybe incorrect due to unknown reason
                        await Task.Delay(10);
                        continue;
                    }
                    var source = new SoftwareBitmapLuminanceSource(bitmap);
                    binarizer = new HybridBinarizer(source);
                    var results = reader.decodeMultiple(new BinaryBitmap(binarizer));
                    if (results != null && results.Length > 0)
                    {
                        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            foreach (var result in results)
                            {
                                if (!readed.Contains(result.Text))
                                {
                                    readed.Add(result.Text);
                                    Textbox.Text += result.Text + "\n";
                                }
                            }
                        });
                    }
                    watch.Stop();
                    int elapsed = (int)watch.ElapsedMilliseconds;
                    //run at max 5Hz
                    await Task.Delay(Math.Max(0, 200 - elapsed));
                }
            });
        }

        async void ReceiveDecodedData(byte[] data, int width, int height)
        {
            
            //basically copied from the sample code
            lock (bufLock)
            {
                //lock when updating decoded buffer, as this is run in async
                //some operation in this function might overlap, so operations involving buffer, width and height must be locked
                if (decodedDataBuf == null)
                {
                    decodedDataBuf = data;
                }
                else
                {
                    if (data.Length != decodedDataBuf.Length)
                    {
                        Array.Resize(ref decodedDataBuf, data.Length);
                    }
                    data.CopyTo(decodedDataBuf.AsBuffer());
                    this.width = width;
                    this.height = height;
                    //System.Diagnostics.Debug.Write(width.ToString() + "\n");
                    //System.Diagnostics.Debug.Write(height.ToString() + "\n");
                }
            }
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                //dispatch to UI thread to do UI update (image)
                //WriteableBitmap is exclusive to UI thread
                if (VideoSource == null || VideoSource.PixelWidth != width || VideoSource.PixelHeight !=  height)
                {
                    VideoSource = new WriteableBitmap((int)width, (int)height);
                    fpvImage.Source = VideoSource;
                    //Start barcode reader worker after the first frame is received
                    if (readerWorker == null)
                    {
                        createWorker();
                        readerWorker.Start();
                    }
                }
                lock (bufLock)
                {
                    //copy buffer to the bitmap and draw the region we will read on to notify the users
                    decodedDataBuf.AsBuffer().CopyTo(VideoSource.PixelBuffer);
                }
                //Invalidate cache and trigger redraw
                VideoSource.Invalidate();
            });
        }

        private void Stop_Button_Click(object sender, RoutedEventArgs e)
        {
            var throttle = 0;
            var roll = 0;
            var pitch = 0;
            var yaw = 0;

            try
            {
                if (DJISDKManager.Instance != null)
                    DJISDKManager.Instance.VirtualRemoteController.UpdateJoystickValue(throttle, yaw, pitch, roll);
            }
            catch (Exception err)
            {
                System.Diagnostics.Debug.WriteLine(err);
            }
        }

        private float throttle = 0;
        private float roll = 0;
        private float pitch = 0;
        private float yaw = 0;

        private async void Grid_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.W:
                case Windows.System.VirtualKey.S:
                    {
                        throttle = 0;
                        break;
                    }
                case Windows.System.VirtualKey.A:
                case Windows.System.VirtualKey.D:
                    {
                        yaw = 0;
                        break;
                    }
                case Windows.System.VirtualKey.I:
                case Windows.System.VirtualKey.K:
                    {
                        pitch = 0;
                        break;
                    }
                case Windows.System.VirtualKey.J:
                case Windows.System.VirtualKey.L:
                    {
                        roll = 0;
                        break;
                    }
                case Windows.System.VirtualKey.Z:
                    {
                        await startPath();
                        break;
                    }
                case Windows.System.VirtualKey.X:
                    {
                        await start12Carton();
                        break;
                    }
                case Windows.System.VirtualKey.C:
                    {
                        await startOverlapColumn();
                        break;
                    }
                case Windows.System.VirtualKey. E:
                    {
                        await start6pic();
                        break;
                    }
                case Windows.System.VirtualKey.V:
                    {
                        await TakeAPic();
                        break; 
                    }
                case Windows.System.VirtualKey.G:
                    {
                        var res = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartTakeoffAsync();
                        break;
                    }
                case Windows.System.VirtualKey.H:
                    {
                        var res = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StartAutoLandingAsync();
                        break;
                    }
                case Windows.System.VirtualKey.P:
                    {
                        var res = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).StopAutoLandingAsync();
                        break; 
                    }
            }

            try
            {
                if (DJISDKManager.Instance != null)
                    DJISDKManager.Instance.VirtualRemoteController.UpdateJoystickValue(throttle, yaw, pitch, roll);
            }
            catch (Exception err)
            {
                System.Diagnostics.Debug.WriteLine(err);
            }
        }

        private async void Grid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                
                case Windows.System.VirtualKey.W:
                    {
                        throttle += 0.02f;
                        if (throttle > 0.5f)
                            throttle = 0.5f;
                        break;
                    }
                case Windows.System.VirtualKey.S:
                    {
                        throttle -= 0.02f;
                        if (throttle < -0.5f)
                            throttle = -0.5f;
                        break;
                    }
                case Windows.System.VirtualKey.A:
                    {
                        yaw -= 0.05f;
                        if (yaw > 0.5f)
                            yaw = 0.5f;
                        break;
                    }
                case Windows.System.VirtualKey.D:
                    {
                        yaw += 0.05f;
                        if (yaw < -0.5f)
                            yaw = -0.5f;
                        break;
                    }
                case Windows.System.VirtualKey.I:
                    {
                        pitch += 0.05f;
                        if (pitch > 0.5)
                            pitch = 0.5f;
                        break;
                    }
                case Windows.System.VirtualKey.K:
                    {
                        pitch -= 0.05f;
                        if (pitch < -0.5f)
                            pitch = -0.5f;
                        break;
                    }
                case Windows.System.VirtualKey.J:
                    {
                       roll -= 0.05f;
                        if (roll < -0.5f)
                            roll = -0.5f;
                        break;
                    }
                case Windows.System.VirtualKey.L:
                    {
                        roll += 0.05f;
                        if (roll > 0.5)
                            roll = 0.5f;
                        break;
                    }
                
                case Windows.System.VirtualKey.Number0:
                    {
                        GimbalAngleRotation rotation = new GimbalAngleRotation()
                        {
                            mode = GimbalAngleRotationMode.RELATIVE_ANGLE,
                            // this sucks
                            //mode = GimbalAngleRotationMode.ABSOLUTE_ANGLE,
                            pitch = 45,
                            roll = 45,
                            yaw = 45,
                            pitchIgnored = false,
                            yawIgnored = false,
                            rollIgnored = false,
                            duration = 0.5
                        };

                        System.Diagnostics.Debug.Write("pitch = 45\n");

                        // Defined somewhere else
                        var gimbalHandler = DJISDKManager.Instance.ComponentManager.GetGimbalHandler(0, 0);

                        //angle
                        //var gimbalRotation = new GimbalAngleRotation();
                        //gimbalRotation.pitch = 45;
                        //gimbalRotation.pitchIgnored = false;
                        //gimbalRotation.duration = 5;
                        //await gimbalHandler.RotateByAngleAsync(gimbalRotation);

                        //Speed
                        var gimbalRotation_speed = new GimbalSpeedRotation();
                        gimbalRotation_speed.pitch = 10;
                        await gimbalHandler.RotateBySpeedAsync(gimbalRotation_speed);

                        //await DJISDKManager.Instance.ComponentManager.GetGimbalHandler(0,0).RotateByAngleAsync(rotation);

                        break;
                    }
                case Windows.System.VirtualKey.P:
                    {
                        GimbalAngleRotation rotation = new GimbalAngleRotation()
                        {
                            mode = GimbalAngleRotationMode.RELATIVE_ANGLE,
                            // this sucks
                            //mode = GimbalAngleRotationMode.ABSOLUTE_ANGLE,
                            pitch = 45,
                            roll = 45,
                            yaw = 45,
                            pitchIgnored = false,
                            yawIgnored = false,
                            rollIgnored = false,
                            duration = 0.5
                        };

                        System.Diagnostics.Debug.Write("pitch = 45\n");

                        // Defined somewhere else
                        var gimbalHandler = DJISDKManager.Instance.ComponentManager.GetGimbalHandler(0, 0);

                        //Speed
                        var gimbalRotation_speed = new GimbalSpeedRotation();
                        gimbalRotation_speed.pitch = -10;
                        await gimbalHandler.RotateBySpeedAsync(gimbalRotation_speed);

                        //await DJISDKManager.Instance.ComponentManager.GetGimbalHandler(0,0).RotateByAngleAsync(rotation);

                        break;
                    }
            }

            try
            {
                if (DJISDKManager.Instance != null)
                    DJISDKManager.Instance.VirtualRemoteController.UpdateJoystickValue(throttle, yaw, pitch, roll);
            }
            catch(Exception err)
            {
                System.Diagnostics.Debug.WriteLine(err);
            }
        }
    }
}
