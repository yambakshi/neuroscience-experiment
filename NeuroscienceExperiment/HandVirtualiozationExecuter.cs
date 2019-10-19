#region Using
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Axiom.Animating;
using Axiom.Core;
using Axiom.Framework.Configuration;
using Axiom.Graphics;
using Axiom.Math;
using Axiom.Media;
using Axiom.RenderSystems.OpenGL;

using OpenTK.Input;

using FDTGloveUltraCSharpWrapper;
using RiftDotNet;

using Material = Axiom.Graphics.Material;
using PixelFormat = Axiom.Media.PixelFormat;
using Texture = Axiom.Core.Texture;
using Viewport = Axiom.Core.Viewport; 
#endregion

namespace NeuroscienceExperiment
{
    public class HandVirtualiozationExecuter
    {
        #region Dlls Import
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr Destination, IntPtr Source, uint Length);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow); 
        #endregion

        #region Members
        private Root m_root;
        private Window m_window;
        private SceneManager m_sceneManager;
        private RenderWindow m_renderWindow;
        private int m_timer = 0;
        private int m_fileIndexL = 0, m_fileIndexR = 0;

        private int numCameras = 0;
        private CLEyeCameraImage m_camLeft, m_camRight;

        private bool m_record, m_renderFromFile;
        private CfdGlove m_gloveR, m_gloveL;
        List<float>[] m_gloveDataL, m_gloveDataR;
        private string[] m_gloveCaptureL, m_gloveCaptureR;

        private Rectangle2D m_rect;
        private Texture m_texture;
        private Material m_axiomMaterial; 

        private OculusDevice m_oculusDevice;
        private Quaternion m_orientation;

        private Camera[] m_camera;
        private Viewport[] m_viewport;

        SceneNode 
            m_armRNode, m_armLNode, 
            m_handRNode, m_handLNode;
        #endregion

        #region Initialize
        public void Initialize(Window window)
        {
            m_window = window;

            InitializeOculus();

            InitializeGloves();

            // Create the root object
            m_root = new Root("GlovesLog.log");

            // Configure the render system
            SetRenderingSystem();

            // Create render window
            m_renderWindow = m_root.Initialize(true);

            // Loads the resources 
            ResourceGroupManager.Instance.AddResourceLocation("media", "Folder", true);
            ResourceGroupManager.Instance.InitializeAllResourceGroups();

            // Create the screen manager
            m_sceneManager = m_root.CreateSceneManager(SceneType.Generic);

            InitializeCameras();

            InitializeViewports();

            InitializeLight();

            // Create material to render ClEye camera input
            m_axiomMaterial = MaterialManager.Instance.Create("dynamicResource", "Materials") as Material;

            // Create texture to render the CLEye camera input to 
            m_texture = TextureManager.Instance.CreateManual("DynamicTexture",
                                                             ResourceGroupManager.DefaultResourceGroupName,
                                                             TextureType.TwoD, 640, 480, 2, PixelFormat.R8G8B8,
                                                             TextureUsage.RenderTarget);

            // Set the cameras in the scene
            SceneNode cameraNode = m_sceneManager.RootSceneNode.CreateChildSceneNode("CameraNode", new Vector3(0, 0, 0));
            cameraNode.AttachObject(m_camera[0]);
            cameraNode.AttachObject(m_camera[1]);

            InitializeEntities();

            // Initialize the material that will draw the camera input
            m_axiomMaterial.GetTechnique(0).GetPass(0).CreateTextureUnitState("DynamicTexture");
            m_axiomMaterial.GetTechnique(0).GetPass(0).DepthCheck = false;
            m_axiomMaterial.GetTechnique(0).GetPass(0).DepthWrite = false;
            m_axiomMaterial.GetTechnique(0).GetPass(0).LightingEnabled = false;

            // Initialize the rectangle input onto which the camera input will be rendered
            m_rect = new Rectangle2D(true);
            //m_rect.SetCorners(-1.15f, 1.0f, 1.15f, -1.0f);
            m_rect.SetCorners(-0.85f, 0.7f, 0.85f, -0.7f);
            m_rect.Material = m_axiomMaterial;
            m_rect.RenderQueueGroup = RenderQueueGroupID.Background;
            SceneNode node = m_sceneManager.RootSceneNode.CreateChildSceneNode("Background");
            node.AttachObject(m_rect);

            // Set the function to the FrameStarted event
            m_root.FrameStarted += FrameStarted;

            InitializeCLEyeCameras();
                        
            // Not recording
            m_record = false;

            // Not loading from file
            m_renderFromFile = false;

            // Initialize the gloves capture  string arrays
            m_gloveCaptureL = new string[30];
            m_gloveCaptureL[0] = "Left Glove Capture";
            m_gloveCaptureL[1] = "\r\n";

            m_gloveCaptureR = new string[30];
            m_gloveCaptureR[0] = "Right Glove Capture";
            m_gloveCaptureR[1] = "\r\n";
        }
        
        private void SetRenderingSystem()
        {
            var renderSystem = m_root.RenderSystems["OpenGL"];
            renderSystem.ConfigOptions["Video Mode"].Value = "1920 x 1080";
            renderSystem.ConfigOptions["Full Screen"].Value = "Yes";
            renderSystem.ConfigOptions["VSync"].Value = "Yes";
            renderSystem.ConfigOptions["FSAA"].Value = "4";
            renderSystem.ConfigOptions["RTT Preferred Mode"].Value = "PBuffer";
            m_root.RenderSystem = renderSystem;
        }
        
        private void InitializeCameras()
        {
            float viewCenter = m_oculusDevice.HScreenSize * 0.25f;
            float eyeProjectionShift = viewCenter - m_oculusDevice.LensSeparationDistance * 0.5f;
            float projectionCenterOffset = 4.0f * eyeProjectionShift / m_oculusDevice.HScreenSize;

            // Create projection matrix
            float aspectRatio = m_oculusDevice.HorizontalResolution / (2 * (float)m_oculusDevice.VerticalResolution);
            float halfScreenDistance = (m_oculusDevice.VScreenSize / 2);
            float yfov = (float)2.0f * (float)Math.Atan(halfScreenDistance / m_oculusDevice.EyeToScreenDistance);
            Matrix4 projMatrix = m_root.RenderSystem.MakeProjectionMatrix(yfov, aspectRatio, 0.3f, 1000.0f);

            //Matrix4 projMatrixL = Matrix4.Compose(new Vector3(projectionCenterOffset, 0, 0), new Vector3(1.0f, 1.0f, 1.0f), Quaternion.Identity);
            //projMatrixL = Matrix4.Multiply(projCenterMatrix, projMatrixL);

            //Matrix4 projMatrixR = Matrix4.Compose(new Vector3(-projectionCenterOffset, 0, 0), new Vector3(1.0f, 1.0f, 1.0f), Quaternion.Identity) * projCenterMatrix;

            // Create view matrix
            float halfIPD = m_oculusDevice.InterpupillaryDistance * 0.5f;
            Matrix4 viewMatrixL = Matrix4.Compose(new Vector3(halfIPD, 0, 0), new Vector3(1.0f, 1.0f, 1.0f), Quaternion.Identity) * viewCenter;
            Matrix4 viewMatrixR = Matrix4.Compose(new Vector3(-halfIPD, 0, 0), new Vector3(1.0f, 1.0f, 1.0f), Quaternion.Identity) * viewCenter;
            
            // Create cameras array
            m_camera = new Camera[2];

            // Left camera
            m_camera[0] = m_sceneManager.CreateCamera("LeftCamera");
            m_camera[0].Move(new Vector3(0, 0, 1));
            m_camera[0].SetCustomProjectionMatrix(true, projMatrix);
            //m_camera[0].SetCustomViewMatrix(true, viewMatrixL);

            // Right camera
            m_camera[1] = m_sceneManager.CreateCamera("RightCamera");
            m_camera[1].Move(new Vector3(0, 0, 1));
            m_camera[1].SetCustomProjectionMatrix(true, projMatrix);
            //m_camera[1].SetCustomViewMatrix(true, viewMatrixR);
        }

        private void InitializeViewports()
        {
            // Create viewports array
            m_viewport = new Viewport[2];

            // Left viewport
            m_viewport[0] = m_renderWindow.AddViewport(m_camera[0], 0.0f, 0, 0.5f, 1.0f, 0);
            //m_viewport[0].BackgroundColor = ColorEx.Red;

            // Right viewport
            m_viewport[1] = m_renderWindow.AddViewport(m_camera[1], 0.45f, 0, 0.5f, 1.0f, 1);
            //m_viewport[1].BackgroundColor = ColorEx.CornflowerBlue;
        }

        private void InitializeLight()
        {
            m_sceneManager.AmbientLight = new ColorEx(1, 1, 1);
            Light light = m_sceneManager.CreateLight("MainLight");
            light.Type = LightType.Point;
            light.Diffuse = ColorEx.White;
            light.Specular = ColorEx.White;
            light.Position = new Vector3(0, -1, 0);
        }

        private void InitializeEntities()
        {
            // Right Arm
            Entity ArmREnt = m_sceneManager.CreateEntity("ArmR", "Arm.mesh");
            ArmREnt.RenderQueueGroup = RenderQueueGroupID.Nine;
            m_armRNode = m_sceneManager.RootSceneNode.CreateChildSceneNode("ArmRNode", new Vector3(1.73, -8, -290));
            m_armRNode.AttachObject(ArmREnt);
            m_armRNode.Roll(100);
            m_armRNode.Pitch(-90);
            m_armRNode.Roll(20);
            m_armRNode.ScaleBy(new Vector3(0.4, 0.4, 0.4));

            // Right Hand
            Entity rightHandEnt = m_sceneManager.CreateEntity("RightHand", "5DTRHand.mesh");
            rightHandEnt.RenderQueueGroup = RenderQueueGroupID.Nine;
            m_handRNode = m_sceneManager.RootSceneNode.CreateChildSceneNode("RightHandNode", new Vector3(1.5, -6.9, -300));
            m_handRNode.AttachObject(rightHandEnt);
            m_handRNode.Roll(100);
            m_handRNode.Pitch(-90);
            m_handRNode.Roll(20);

            // Left arm
            Entity ArmLEnt = m_sceneManager.CreateEntity("ArmL", "Arm.mesh");
            ArmLEnt.RenderQueueGroup = RenderQueueGroupID.Nine;
            m_armLNode = m_sceneManager.RootSceneNode.CreateChildSceneNode("ArmLNode", new Vector3(-1.73, -8, -290));
            m_armLNode.AttachObject(ArmLEnt);
            m_armLNode.Roll(80);
            m_armLNode.Pitch(-90);
            m_armLNode.Roll(20);
            m_armLNode.ScaleBy(new Vector3(0.4, 0.4, 0.4));

            // Left hand            
            Entity leftHandEnt = m_sceneManager.CreateEntity("LeftHand", "5DTLHand.mesh");
            leftHandEnt.RenderQueueGroup = RenderQueueGroupID.Nine;
            m_handLNode = m_sceneManager.RootSceneNode.CreateChildSceneNode("LeftHandNode", new Vector3(-1.5, -6.9, -300));
            m_handLNode.AttachObject(leftHandEnt);
            m_handLNode.Roll(80);
            m_handLNode.Pitch(-90);
            m_handLNode.Roll(20);
            
            // Left Camera
            //m_camera[0].LookAt(rightHandEnt.BoundingBox.Center);

            // Right camera
            //m_camera[1].LookAt(rightHandEnt.BoundingBox.Center);
        }

        private void InitializeOculus()
        {
            // Initialize the oculus device
            m_oculusDevice = new OculusDevice();
            m_oculusDevice.Initialize();

            m_orientation = new Quaternion(
                m_oculusDevice.m_hmd.Orientation.W,
                m_oculusDevice.m_hmd.Orientation.X,
                m_oculusDevice.m_hmd.Orientation.Y,
                m_oculusDevice.m_hmd.Orientation.Z);
        }

        private void InitializeGloves()
        {
            m_gloveR = new CfdGlove();
            m_gloveR.Open("USB0");

            m_gloveL = new CfdGlove();
            m_gloveL.Open("USB1");
        }

        private void InitializeCLEyeCameras()
        {
            // Query for number of connected cameras
            numCameras = CLEyeCameraDevice.CameraCount;
            m_camLeft = new CLEyeCameraImage
            {
                //Margin = new Thickness(0,0,640,135),
                //HorizontalAlignment = HorizontalAlignment.Stretch,
                //VerticalAlignment = VerticalAlignment.Stretch,
                Framerate = 30,
                ColorMode = CLEyeCameraColorMode.CLEYE_COLOR_PROCESSED,
                Resolution = CLEyeCameraResolution.CLEYE_VGA,
                //Stretch = Stretch.Fill
            };

            m_camRight = new CLEyeCameraImage()
            {
                //Margin = new Thickness(587, 146, 26, 135),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Framerate = 30,
                ColorMode = CLEyeCameraColorMode.CLEYE_COLOR_PROCESSED,
                Resolution = CLEyeCameraResolution.CLEYE_VGA,
                Stretch = Stretch.Fill
            };
        }
        #endregion

        #region Run
        public void Run()
        {
            try
            {
                if (numCameras == 0)
                {
                    MessageBox.Show("Could not find any PS3Eye cameras!");
                    return;
                }

                // Create cameras, set some parameters and start capture
                if (numCameras >= 1)
                {
                    // Start rolling
                    m_camLeft.Device.Create(CLEyeCameraDevice.CameraUUID(0), this);
                    m_camLeft.Device.Start();

                    // Balances the exposure
                    m_camLeft.Device.AutoExposure = true;

                    // Gives the "fish-eye" effect
                    m_camLeft.Device.LensCorrection1 = 100;
                    m_camLeft.Device.LensCorrection2 = 100;
                    m_camLeft.Device.LensCorrection3 = 100;
                }
                if (numCameras == 2)
                {
                    m_camRight.Device.Create(CLEyeCameraDevice.CameraUUID(1), this);
                    m_camRight.Device.Start();
                }
            }
            catch (Exception e)
            {

                // do nothing in case rendering is fucked
            }
        }

        private void FrameStarted(object sender, FrameEventArgs e)
        {
            // If its render from file rendering modemode
            if (m_renderFromFile)
            {
                if (m_timer % 5 == 0)
                {
                    UpdateRightHandFromFile();
                    UpdateLeftHandFromFile();
                    m_timer = 0;
                }
                m_timer++;
            }
            // Or its gloves input
            else
            {
                UpdateRightHand();
                UpdateLeftHand();
            }

            // Handle keyboard input
            KeyboardInput();
        }

        private void KeyboardInput()
        {
            var state = OpenTK.Input.Keyboard.GetState();

            #region Record
            // Toggle recording
            if (state[OpenTK.Input.Key.Space])
            {
                if (!m_renderFromFile)
                {
                    // If recording ended it is saved
                    // as text file
                    if (m_record)
                    {
                        m_record = false;
                        SaveCapture();
                    }
                    // Start recording
                    else
                        m_record = true;
                }

                // Puase the thread so user will have time
                // to release the key, so the keyboard state will change
                Thread.Sleep(200);
            } 
            #endregion

            #region Last Capture
            // Toggle render from file
            if (state[OpenTK.Input.Key.L])
            {
                // End rendering from file
                if (m_renderFromFile)
                    m_renderFromFile = false;
                // Render from file
                else
                {
                    m_renderFromFile = true;
                    m_fileIndexR = 0;
                    m_fileIndexL = 0;

                    // Get the current directory
                    string dir = Directory.GetCurrentDirectory();

                    // Get all text files names in the current directory
                    string[] fileNames = Directory.GetFiles(dir, "*.txt");

                    // Load the last capture file
                    LoadGlovesData(fileNames.Length / 2);
                }

                // Puase the thread so user will have time
                // to release the key, so the keyboard state will change
                Thread.Sleep(200);
            } 
            #endregion

            #region Open
            // Choose glove capture from file dialog
            if (state[OpenTK.Input.Key.O])
            {
                System.Windows.Forms.OpenFileDialog fileDialog = new System.Windows.Forms.OpenFileDialog();
                fileDialog.Filter = "Text|*.txt|All|*.*";
                if (fileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // get the chosen file name                    
                    string file = fileDialog.FileName;

                    // Get the the capture number from the file name
                    char num = file[file.Length - 5];

                    // Load teh specified capture
                    LoadGlovesData(int.Parse(num.ToString()));
                    m_fileIndexR = 0;
                    m_fileIndexL = 0;

                    // Render from file
                    m_renderFromFile = true;
                }

                // Puase the thread so user will have time
                // to release the key, so the keyboard state will change
                Thread.Sleep(200);
            } 
            #endregion

            #region Escape
            // If Escape key is pressed, kill application
            if (state[OpenTK.Input.Key.Escape])
            {
                // Unregister to the render frame event
                m_root.FrameStarted -= FrameStarted;

                // End rendering
                m_root.QueueEndRendering();

                // Close window
                m_window.Close();
            } 
            #endregion
        }

        public void CameraDataUpdate(InteropBitmap cameraInterop)
        {
            // Stores the cam input as bitmap source
            var imageSource = cameraInterop as BitmapSource;

            // Creates an jpeg encoder 
            BitmapEncoder enc = new JpegBitmapEncoder();

            // Create bitmap frame from the image source
            enc.Frames.Add(BitmapFrame.Create(imageSource));

            // Saves the frame to a memory stream
            var ms = new MemoryStream();
            enc.Save(ms);

            // Create a bitmap from the memory stream
            var bitmap = new Bitmap(ms);

            // Get width and height
            int width = bitmap.Width;
            int height = bitmap.Height;

            // Gets the buffer from the texture
            var texBuffer = m_texture.GetBuffer();

            // Locks the buffer so we can copy data into the buffer
            texBuffer.Lock(BufferLocking.Discard);
            PixelBox pb = texBuffer.CurrentLock;
            BitmapData data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height),
                                                ImageLockMode.ReadOnly,
                                                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            // Copy the data into the buffer
            CopyMemory(pb.Data, data.Scan0, (uint)((width) * height * 4));

            // Unlock the buffer
            bitmap.UnlockBits(data);
            texBuffer.Unlock();

            // Render using the material
            m_axiomMaterial.GetTechnique(0).GetPass(0).RemoveAllTextureUnitStates();
            m_axiomMaterial.GetTechnique(0).GetPass(0).CreateTextureUnitState("DynamicTexture");

            // Update the camera orientation
            UpdateCameraOrientation();

            try
            {
                // Render a single frame
                m_root.RenderOneFrame();
            }
            catch(Exception e)
            {
                return;
            }
        }
        
        private void UpdateCameraOrientation()
        {
            float w, x, y, z;

            w = m_oculusDevice.m_hmd.Orientation.W;
            x = m_oculusDevice.m_hmd.Orientation.X;
            y = m_oculusDevice.m_hmd.Orientation.Y;
            z = m_oculusDevice.m_hmd.Orientation.Z;
            
            // Left camera
            m_camera[0].Orientation = new Quaternion(1.0f, x / 40, m_orientation.y, m_orientation.z);
            
            // Right camera
            m_camera[1].Orientation = new Quaternion(1.0f, x / 40, m_orientation.y, m_orientation.z);
        }

        private void CalibrateGloves()
        {
            List<string> lstSensors = new List<string>();

            if (m_gloveL == null)
                return;

            lstSensors.Clear();

            ushort[] arr = new ushort[20];
            float[] farr = new float[20];

            //fdGlove.GetSensor GetSensorRawAll(arr);
            m_gloveL.GetSensorScaledAll(ref farr);
            m_gloveL.GetSensorRawAll(ref arr);

            ushort[] upperVals = new ushort[10];
            ushort[] lowerVals = new ushort[10];

            ushort[] setupperVals = new ushort[15];
            ushort[] setlowerVals = new ushort[15];

            m_gloveL.GetCalibrationAll(ref upperVals, ref lowerVals);

            //for (int i = 0; i < fdGlove.GetNumSensors(); ++i)
            if (m_gloveL.IsOpen())
            {
                for (int i = 0; i < 18; ++i)
                {
                    // Reading single values at a time - this is actually a bit faster than reading the whole array
                    // because more marshalling intensive operations need to be preformed when dealing with arrays. (driver is written in unmanaged code)                
                    //ushort a = fdGlove.GetSensorRaw(i);
                    //float f = fdGlove.GetSensorScaled(i);
                    //lstSensors.Items.Add("Sensor " + i + " - Scaled: " + String.Format("{0:0.00}", f) + " ( Raw: " + a + ")");
                    lstSensors.Add("Sensor " + i + " - Scaled: " + String.Format("{0:0.00}", farr[i]) + " ( Raw: " + arr[i] + ") Cal:(" + lowerVals[i] + "," + upperVals[i] + ")");
                }

            }
            string gesture = m_gloveL.GetGesture().ToString();
            string packetRate = m_gloveL.GetPacketRate().ToString();
        }

        float[] prevData;
        private void UpdateLeftHand()
        {
            // Get the finger values from sensors
            var scaledSensors = new float[20];
            m_gloveR.GetSensorScaledAll(ref scaledSensors);

            // Get the hand skeleton
            Skeleton skel = m_sceneManager.GetEntity("LeftHand").Skeleton;
            int iSignMsg = 1;

            #region Middle Finger
            // UPDATE MIDDLE FINGER

            // middle finger - first bone
            float fMiddleFingerNear = 90 * scaledSensors[(int)EfdSensors.FD_MIDDLENEAR];
            Bone mBone = skel.GetBone("Middle.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(0);

            // middle finger - second bone
            float fMiddleFingerFar = 90 * scaledSensors[(int)EfdSensors.FD_MIDDLEFAR];
            fMiddleFingerFar += (fMiddleFingerFar/50.0f) * 20;
            mBone = skel.GetBone("Middle.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            if(fMiddleFingerFar > 50)
                mBone.Pitch(-fMiddleFingerFar);

            // middle finger - third bone
            float fMiddleFingerRing = 90 * scaledSensors[(int)EfdSensors.FD_MIDDLENEAR];
            mBone = skel.GetBone("Middle.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            if(fMiddleFingerRing > 20)
                mBone.Pitch(-fMiddleFingerRing);
            #endregion

            #region Ring Finger
            // UPDATE RING FINGER

            float fMiddleRing = iSignMsg * (15 * scaledSensors[(int)EfdSensors.FD_MIDDLERING]);
            float fRingFingerNear = 90 * scaledSensors[(int)EfdSensors.FD_RINGNEAR];

            // ring finger - first bone
            mBone = skel.GetBone("Ring.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(0);

            // ring finger - second bone
            float fRingFingerFar = 90 * scaledSensors[(int)EfdSensors.FD_RINGFAR];
            float thirdBoneRing = fRingFingerFar;
            mBone = skel.GetBone("Ring.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            fRingFingerFar += (fRingFingerFar / 50.0f) * 20;
            if(fRingFingerFar > 60)
                mBone.Pitch(-fRingFingerFar);

            // ring finger - third bone
            mBone = skel.GetBone("Ring.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            //if (fRingFingerFar < 60)
                mBone.Pitch(-thirdBoneRing);
            #endregion

            #region Thumb Finger
            // UPDATE THUMB FINGER

            float fThumbNear = 120 * scaledSensors[(int)EfdSensors.FD_THUMBNEAR] - 50;

            // thumb finger - first bone
            mBone = skel.GetBone("Thumb.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            //if (fThumbNear > -3)
                //mBone.Pitch(-fThumbNear / 3);
            mBone.Pitch(0);

            // thumb finger - second bone
            float fThumbFar = 90 * scaledSensors[(int)EfdSensors.FD_THUMBFAR] - 13;
            mBone = skel.GetBone("Thumb.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            if (fThumbFar > -5)
                mBone.Pitch(-fThumbFar);

            // thumb finger - third bone
            mBone = skel.GetBone("Thumb.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            if (fThumbFar > -10)
                mBone.Pitch(-fThumbFar);
            #endregion

            #region Index Finger
            // UPDATE INDEX FINGER

            float fIndexFingerNear = 90 * scaledSensors[(int)EfdSensors.FD_INDEXNEAR];
            float fIndexFingerMiddle = iSignMsg * (30 * scaledSensors[(int)EfdSensors.FD_INDEXMIDDLE]);

            // Index finger - first bone
            mBone = skel.GetBone("Index.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fIndexFingerNear);

            // Index finger - second bone
            float fIndexFingerFar = 90 * scaledSensors[(int)EfdSensors.FD_INDEXFAR];
            mBone = skel.GetBone("Index.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fIndexFingerFar);

            // Index finger - third bone
            mBone = skel.GetBone("Index.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fIndexFingerFar);
            #endregion

            #region Little Finger
            // UPDATE LITTLE FINGER

            float fLittleFingerNear = 90 * scaledSensors[(int)EfdSensors.FD_LITTLENEAR];
            float fRingLittle = iSignMsg * (30 * scaledSensors[(int)EfdSensors.FD_RINGLITTLE] - 18);

            // little finger - first bone
            mBone = skel.GetBone("Little.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fLittleFingerNear);
            //mBone.Roll(-fRingLittle - fMiddleRing);
            //mBone.Roll(-fRingLittle - fMiddleRing + 12);

            // little finger - second bone
            float fLittleFingerFar = 90 * scaledSensors[(int)EfdSensors.FD_LITTLEFAR];
            mBone = skel.GetBone("Little.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fLittleFingerFar);

            // little finger - third bone
            mBone = skel.GetBone("Little.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fLittleFingerFar);
            #endregion

            // Store values for text file "GlovesLog"
            if (m_record)
            {
                // Middle finger
                m_gloveCaptureR[1] += 0 + ",";
                m_gloveCaptureR[3] += -fMiddleFingerFar + ",";
                m_gloveCaptureR[5] += -fMiddleFingerRing + ",";

                m_gloveCaptureR[6] = "/////////////////////////////////";

                // Ring finger
                m_gloveCaptureR[7] += 0 + ",";
                m_gloveCaptureR[9] += -fRingFingerFar + ",";
                m_gloveCaptureR[11] += -fRingFingerFar + ",";

                m_gloveCaptureR[12] = "/////////////////////////////////";

                // Thumb finger
                m_gloveCaptureR[13] += (-fThumbNear / 3) + ",";
                m_gloveCaptureR[15] += -fThumbFar + ",";
                m_gloveCaptureR[17] += -fThumbFar + ",";

                m_gloveCaptureR[18] = "/////////////////////////////////";

                // Thumb finger
                m_gloveCaptureR[19] += -fIndexFingerNear + ",";
                m_gloveCaptureR[21] += -fIndexFingerFar + ",";
                m_gloveCaptureR[23] += -fIndexFingerFar + ",";

                m_gloveCaptureR[24] = "/////////////////////////////////";

                // Little finger
                m_gloveCaptureR[25] += -fLittleFingerNear + ",";
                m_gloveCaptureR[27] += -fLittleFingerFar + ",";
                m_gloveCaptureR[29] += -fLittleFingerFar + ",";
            }

            prevData = scaledSensors;
        }

        private void UpdateRightHand()
        {
            // Get the finger values from sensors
            var scaledSensors = new float[20];
            m_gloveL.GetSensorScaledAll(ref scaledSensors);

            // Get the hand skeleton
            Skeleton skel = m_sceneManager.GetEntity("RightHand").Skeleton;
            int iSignMsg = 1;

            #region Middle Finger
            // UPDATE MIDDLE FINGER

            // middle finger - first bone
            float fMiddleFingerNear = 90 * scaledSensors[(int)EfdSensors.FD_MIDDLENEAR];
            Bone mBone = skel.GetBone("Middle.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fMiddleFingerNear);

            // middle finger - second bone
            float fMiddleFingerFar = 90 * scaledSensors[(int)EfdSensors.FD_MIDDLEFAR];
            mBone = skel.GetBone("Middle.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fMiddleFingerFar);

            // middle finger - third bone
            float fMiddleFingerRing = 90 * scaledSensors[(int)EfdSensors.FD_MIDDLENEAR];
            mBone = skel.GetBone("Middle.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fMiddleFingerRing);
            #endregion

            #region Ring Finger
            // UPDATE RING FINGER

            float fMiddleRing = iSignMsg * (15 * scaledSensors[(int)EfdSensors.FD_MIDDLERING]);
            float fRingFingerNear = 90 * scaledSensors[(int)EfdSensors.FD_RINGNEAR];

            // ring finger - first bone
            mBone = skel.GetBone("Ring.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fRingFingerNear);
            //mBone.Roll(-fMiddleRing);

            // ring finger - second bone
            float fRingFingerFar = 90 * scaledSensors[(int)EfdSensors.FD_RINGFAR];
            mBone = skel.GetBone("Ring.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fRingFingerFar);

            // ring finger - third bone
            mBone = skel.GetBone("Ring.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fRingFingerFar);
            #endregion

            #region Thumb Finger
            // UPDATE THUMB FINGER

            float fThumbNear = 120 * scaledSensors[(int)EfdSensors.FD_THUMBNEAR] - 50;

            // thumb finger - first bone
            mBone = skel.GetBone("Thumb.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            if(fThumbNear > -2)
                mBone.Pitch(-fThumbNear / 3);

            // thumb finger - second bone
            float fThumbFar = 90 * scaledSensors[(int)EfdSensors.FD_THUMBFAR] - 13;
            mBone = skel.GetBone("Thumb.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            if (fThumbFar > -2)
                mBone.Pitch(-fThumbFar);

            // thumb finger - third bone
            mBone = skel.GetBone("Thumb.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fThumbFar);
            #endregion

            #region Index Finger
            // UPDATE INDEX FINGER

            float fIndexFingerNear = 90 * scaledSensors[(int)EfdSensors.FD_INDEXNEAR];
            float fIndexFingerMiddle = iSignMsg * (30 * scaledSensors[(int)EfdSensors.FD_INDEXMIDDLE]);

            // Index finger - first bone
            mBone = skel.GetBone("Index.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fIndexFingerNear);

            // Index finger - second bone
            float fIndexFingerFar = 90 * scaledSensors[(int)EfdSensors.FD_INDEXFAR];
            mBone = skel.GetBone("Index.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fIndexFingerFar);

            // Index finger - third bone
            mBone = skel.GetBone("Index.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fIndexFingerFar);
            #endregion

            #region Little Finger
            // UPDATE LITTLE FINGER

            float fLittleFingerNear = 90 * scaledSensors[(int)EfdSensors.FD_LITTLENEAR];
            float fRingLittle = iSignMsg * (30 * scaledSensors[(int)EfdSensors.FD_RINGLITTLE] - 18);

            // little finger - first bone
            mBone = skel.GetBone("Little.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fLittleFingerNear);

            // little finger - second bone
            float fLittleFingerFar = 90 * scaledSensors[(int)EfdSensors.FD_LITTLEFAR];
            mBone = skel.GetBone("Little.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fLittleFingerFar);

            // little finger - third bone
            mBone = skel.GetBone("Little.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(-fLittleFingerFar);
            #endregion

            // Store values for text file "GlovesLog"
            if (m_record)
            {
                // Middle finger
                m_gloveCaptureL[1] += -fMiddleFingerNear + ",";
                m_gloveCaptureL[3] += -fMiddleFingerFar + ",";
                m_gloveCaptureL[5] += -fMiddleFingerRing + ",";

                m_gloveCaptureL[6] = "/////////////////////////////////";

                // Ring finger
                m_gloveCaptureL[7] += -fRingFingerNear + ",";
                m_gloveCaptureL[9] += -fRingFingerFar + ",";
                m_gloveCaptureL[11] += -fRingFingerFar + ",";

                m_gloveCaptureL[12] = "/////////////////////////////////";

                // Thumb finger
                m_gloveCaptureL[13] += (-fThumbNear / 3) + ",";
                m_gloveCaptureL[15] += -fThumbFar + ",";
                m_gloveCaptureL[17] += -fThumbFar + ",";

                m_gloveCaptureL[18] = "/////////////////////////////////";

                // Thumb finger
                m_gloveCaptureL[19] += -fIndexFingerNear + ",";
                m_gloveCaptureL[21] += -fIndexFingerFar + ",";
                m_gloveCaptureL[23] += -fIndexFingerFar + ",";

                m_gloveCaptureL[24] = "/////////////////////////////////";

                // Little finger
                m_gloveCaptureL[25] += -fLittleFingerNear + ",";
                m_gloveCaptureL[27] += -fLittleFingerFar + ",";
                m_gloveCaptureL[29] += -fLittleFingerFar + ",";
            }
        }

        private void UpdateRightHandFromFile()
        {
            if (m_gloveDataL == null)
            {
                m_renderFromFile = false;
                return;
            }

            // Get the hand skeleton
            Skeleton skel = m_sceneManager.GetEntity("RightHand").Skeleton;

            #region Middle Finger
            // UPDATE MIDDLE FINGER

            // middle finger - first bone
            Bone mBone = skel.GetBone("Middle.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[0][m_fileIndexL]);

            // middle finger - second bone
            mBone = skel.GetBone("Middle.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[2][m_fileIndexL]);

            // middle finger - third bone
            mBone = skel.GetBone("Middle.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[4][m_fileIndexL]);
            #endregion

            #region Ring Finger
            // UPDATE RING FINGER

            // ring finger - first bone
            mBone = skel.GetBone("Ring.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[6][m_fileIndexL]);

            // ring finger - second bone
            mBone = skel.GetBone("Ring.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[8][m_fileIndexL]);

            // ring finger - third bone
            mBone = skel.GetBone("Ring.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[10][m_fileIndexL]);
            #endregion

            #region Thumb Finger
            // UPDATE THUMB FINGER

            // thumb finger - first bone
            mBone = skel.GetBone("Thumb.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[12][m_fileIndexL]);
            //mBone.Roll(fTumbIndex);

            // thumb finger - second bone
            mBone = skel.GetBone("Thumb.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[14][m_fileIndexL]);

            // thumb finger - third bone
            mBone = skel.GetBone("Thumb.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[16][m_fileIndexL]);
            #endregion

            #region Index Finger
            // UPDATE INDEX FINGER

            // Index finger - first bone
            mBone = skel.GetBone("Index.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[18][m_fileIndexL]);

            // Index finger - second bone
            mBone = skel.GetBone("Index.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[20][m_fileIndexL]);

            // Index finger - third bone
            mBone = skel.GetBone("Index.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[22][m_fileIndexL]);
            #endregion

            #region Little Finger
            // UPDATE LITTLE FINGER

            // little finger - first bone
            mBone = skel.GetBone("Little.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[24][m_fileIndexL]);

            // little finger - second bone
            mBone = skel.GetBone("Little.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[26][m_fileIndexL]);

            // little finger - third bone
            mBone = skel.GetBone("Little.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataL[28][m_fileIndexL]);
            #endregion

            // If reached the end of capture file, start again
            if (m_gloveDataL[0].Count - 1 > m_fileIndexL)
                m_fileIndexL++;
            else
                m_fileIndexL = 0;
        }
        
        private void UpdateLeftHandFromFile()
        {
            if (m_gloveDataR == null)
            {
                m_renderFromFile = false;
                return;
            }

            // Get the hand skeleton
            Skeleton skel = m_sceneManager.GetEntity("LeftHand").Skeleton;

            #region Middle Finger
            // UPDATE MIDDLE FINGER

            // middle finger - first bone
            Bone mBone = skel.GetBone("Middle.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[0][m_fileIndexR]);

            // middle finger - second bone
            mBone = skel.GetBone("Middle.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[2][m_fileIndexR]);

            // middle finger - third bone
            mBone = skel.GetBone("Middle.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[4][m_fileIndexR]);
            #endregion

            #region Ring Finger
            // UPDATE RING FINGER

            // ring finger - first bone
            mBone = skel.GetBone("Ring.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[6][m_fileIndexR]);

            // ring finger - second bone
            mBone = skel.GetBone("Ring.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[8][m_fileIndexR]);

            // ring finger - third bone
            mBone = skel.GetBone("Ring.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[10][m_fileIndexR]);
            #endregion

            #region Thumb Finger
            // UPDATE THUMB FINGER

            // thumb finger - first bone
            mBone = skel.GetBone("Thumb.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[12][m_fileIndexR]);
            //mBone.Roll(fTumbIndex);

            // thumb finger - second bone
            mBone = skel.GetBone("Thumb.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[14][m_fileIndexR]);

            // thumb finger - third bone
            mBone = skel.GetBone("Thumb.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[16][m_fileIndexR]);
            #endregion

            #region Index Finger
            // UPDATE INDEX FINGER

            // Index finger - first bone
            mBone = skel.GetBone("Index.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[18][m_fileIndexR]);

            // Index finger - second bone
            mBone = skel.GetBone("Index.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[20][m_fileIndexR]);

            // Index finger - third bone
            mBone = skel.GetBone("Index.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[22][m_fileIndexR]);
            #endregion

            #region Little Finger
            // UPDATE LITTLE FINGER

            // little finger - first bone
            mBone = skel.GetBone("Little.1");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[24][m_fileIndexR]);

            // little finger - second bone
            mBone = skel.GetBone("Little.2");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[26][m_fileIndexR]);

            // little finger - third bone
            mBone = skel.GetBone("Little.3");
            mBone.IsManuallyControlled = true;
            mBone.Reset();
            mBone.Pitch(m_gloveDataR[28][m_fileIndexR]);
            #endregion

            // If reached the end of capture file, start again
            if (m_gloveDataR[0].Count - 1 > m_fileIndexR)
                m_fileIndexR++;
            else
                m_fileIndexR = 0;
        }

        private void SaveCapture()
        {
            // Get the current directory
            string dir = Directory.GetCurrentDirectory();

            // Get all text files names in the current directory
            string[] fileNames = Directory.GetFiles(dir, "*.txt");

            // Save the captures as text files
            System.IO.File.WriteAllLines(dir + "\\Left Glove Capture " + ((fileNames.Length/2) + 1) + ".txt", m_gloveCaptureL);
            System.IO.File.WriteAllLines(dir + "\\Right Glove Capture " + ((fileNames.Length / 2) + 1) + ".txt", m_gloveCaptureR);
        }
        
        private void LoadGlovesData(int captureNumber)
        {
            string[] capture;

            // Get the current directory 
            string dir = Directory.GetCurrentDirectory();

            // Get the left glove data from the chosen file
            try
            {
                capture = System.IO.File.ReadAllLines(dir + "\\Left Glove Capture " + captureNumber + ".txt");
            }
            catch
            {
                return;
            }

            // Create the gloves data list
            m_gloveDataL = new List<float>[capture.Length - 2];

            // Goes through the capture and get the values
            for (int i = 0; i < capture.Length - 2; i++)
            {
                // New list for every bone
                m_gloveDataL[i] = new List<float>();

                // Index in string
                int j = 0;

                // While its not the end of the values for the current bone
                while (capture[i + 2].Length > j && capture[i + 2][j] != '/')
                {
                    // Get the value
                    string val = "";
                    while (capture[i + 2][j] != ',')
                    {
                        val += capture[i + 2][j];
                        j++;
                    }
                    j++;

                    // Add the value to the list
                    m_gloveDataL[i].Add(float.Parse(val));
                }
            }

            // Get the right glove data from the chosen file
            try
            {
                capture = System.IO.File.ReadAllLines(dir + "\\Right Glove Capture " + captureNumber + ".txt");
            }
            catch
            {
                return;
            }

            // Create the gloves data list
            m_gloveDataR = new List<float>[capture.Length - 2];

            // Goes through the capture and get the values
            for (int i = 0; i < capture.Length - 2; i++)
            {
                // New list for every bone
                m_gloveDataR[i] = new List<float>();

                // Index in string
                int j = 0;

                // While its not the end of the values for the current bone
                while (capture[i + 2].Length > j && capture[i + 2][j] != '/')
                {
                    // Get the value
                    string val = "";
                    while (capture[i + 2][j] != ',')
                    {
                        val += capture[i + 2][j];
                        j++;
                    }
                    j++;

                    // Add the value to the list
                    m_gloveDataR[i].Add(float.Parse(val));
                }
            }
        }
        #endregion

        #region Shutdown
        public void Shutdown()
        {
            if (m_root == null) 
                return;
            try
            {
                m_root.SuspendRendering = true;
                m_root.DetachRenderTarget(m_renderWindow);
                m_renderWindow.Dispose();
                m_root.Dispose();
                if (numCameras >= 1)
                {
                    m_camLeft.Device.Stop();
                    m_camLeft.Device.Destroy();
                }
                if (numCameras == 2)
                {
                    m_camRight.Device.Stop();
                    m_camRight.Device.Destroy();
                }

                // If shutting down while recording
                // it saves the capture
                if (m_record)
                    SaveCapture();
            }
            catch (Exception e)
            {
                // do nothing in case stop rendering
            }

        } 
        #endregion
    }
}
