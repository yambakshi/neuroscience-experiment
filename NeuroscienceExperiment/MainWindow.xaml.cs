using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Axiom.Core;
using Axiom.Graphics;
using Axiom.Math;
using Axiom.Framework.Configuration;

using FDTGloveUltraCSharpWrapper;
using RiftDotNet;

namespace NeuroscienceExperiment
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        HandVirtualiozationExecuter m_HandVirtualiozationExecuter;
        public MainWindow()
        {
            InitializeComponent();
            
            this.Loaded += new RoutedEventHandler(MainWindow_Loaded);
            this.Closing += new System.ComponentModel.CancelEventHandler(MainWindow_Closing);

            m_HandVirtualiozationExecuter = new HandVirtualiozationExecuter();

            /*
            IConfigurationManager ConfigurationManager = ConfigurationManagerFactory.CreateDefault();
            using (var root = new Root("Game1.log"))
            {
                if (ConfigurationManager.ShowConfigDialog(root))
                {
                    RenderWindow window = root.Initialize(true);
                    ResourceGroupManager.Instance.AddResourceLocation("media", "Folder", true);
                    SceneManager scene = root.CreateSceneManager(SceneType.Generic);
                    Camera camera = scene.CreateCamera("cam1");
                    Viewport viewport = window.AddViewport(camera);
                    TextureManager.Instance.DefaultMipmapCount = 5;
                    ResourceGroupManager.Instance.InitializeAllResourceGroups();
                    Entity penguin = scene.CreateEntity("bob", "penguin.mesh");
                    SceneNode penguinNode = scene.RootSceneNode.CreateChildSceneNode();
                    penguinNode.AttachObject(penguin);
                    camera.Move(new Vector3(0, 0, 300));
                    camera.LookAt(penguin.BoundingBox.Center);
                    root.RenderOneFrame();
                }
            }
            */
        }

        void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            m_HandVirtualiozationExecuter.Initialize(this);
            m_HandVirtualiozationExecuter.Run();
        }

        void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            m_HandVirtualiozationExecuter.Shutdown();
        }
    }
}
