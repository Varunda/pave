using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PlanetsideAwesomeViewingExperience {

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {

        protected OBSWebsocket socket = new OBSWebsocket();

        protected List<SceneItemDetails> cameras = new();
        protected List<SceneBasicInfo> scenes = new();

        protected string previewScene = "";

        protected List<SidecamMeta> sideCams = new();

        public MainWindow() {
            InitializeComponent();
            ConnectWebsocket();
        }

        public void ConnectWebsocket() {
            socket.ConnectAsync("ws://localhost:4455", "");

            socket.Connected += (object? sender, EventArgs e) => {
                Console.WriteLine("connected");

                ObsVersion version = socket.GetVersion();
                Console.WriteLine($"Version: {version.Version}, plugin version: {version.PluginVersion}, studio version {version.OBSStudioVersion}");

                this.scenes = socket.GetSceneList().Scenes;
                Console.WriteLine($"Loaded {this.scenes.Count} scenes");

                List<SceneItemDetails> items = socket.GetSceneItemList("stream sources");
                this.cameras = items.Where(iter => iter.SourceKind == "ffmpeg_source").ToList();
                Console.WriteLine($"Loaded {this.cameras.Count} cameras");

                CamList.Dispatcher.BeginInvoke(() => {
                    CamList.Items.Clear();
                    foreach (SceneItemDetails cam in cameras) {
                        CamList.Items.Add(cam);
                    }
                });

                this.previewScene = socket.GetCurrentPreviewScene();
                PreviewScene.Dispatcher.BeginInvoke(() => {
                    PreviewScene.Content = $"Preview scene: {this.previewScene}";
                });

            };

            socket.CurrentPreviewSceneChanged += (object? sender, CurrentPreviewSceneChangedEventArgs args) => {

                // Remove the side cams as we move away
                foreach (SidecamMeta meta in this.sideCams) {
                    this.socket.RemoveSceneItem(this.previewScene, meta.ItemId);
                }
                this.sideCams.Clear();
                this.UpdateSidecamUI();

                this.previewScene = args.SceneName;
                PreviewScene.Dispatcher.BeginInvoke(() => {
                    PreviewScene.Content = $"Preview scene: {args.SceneName}";
                });

                string[] parts = this.previewScene.Split("-");
                if (parts.Length < 2) {
                    Console.WriteLine($"{this.previewScene} is not a cam scene");
                    return;
                }

                string skippedCam = parts[0].Trim() + " src";

                Console.WriteLine($"Clearing cams from {this.previewScene}, except '{skippedCam}'");

                // Get all sources and remove the ffmpeg ones
                List<SceneItemDetails> sources = socket.GetSceneItemList(this.previewScene);
                foreach (SceneItemDetails source in sources) {
                    if (source.SourceKind != "ffmpeg_source") { // skip non-media source inputs
                        continue;
                    }

                    string n = source.SourceName;
                    //bool isPlayerPov = n == "Cam 1 src" || n == "Cam 2 src" || n == "Cam 3 src";
                    bool isPlayerPov = n == "Cam 1 src" || n == "Cam 2 src" || n == "Cam 7 src" || n == "Cam 8 src";

                    if (source.SourceName == skippedCam) { // skip media source that's the main cam
                        if (!isPlayerPov) {
                            socket.SetInputMute(source.SourceName, false);
                        }
                        continue;
                    }

                    if (source.SourceName == "Realtime map") { // we like the map
                        continue;
                    }

                    socket.RemoveSceneItem(this.previewScene, source.ItemId);
                }
            };
        }

        public void CompositeCamera(int sourceId, string position) {
            Console.WriteLine($"Setting {sourceId} to {position}");
            SceneItemDetails? camera = this.cameras.Find(iter => iter.ItemId == sourceId);
            if (camera == null) {
                Console.WriteLine($"Missing {sourceId}");
                return;
            }

            if (this.socket.IsConnected == false) {
                Console.WriteLine($"socket not connected");
                return;
            }

            List<SceneItemDetails> currentSources = socket.GetSceneItemList(this.previewScene);
            if (currentSources.FirstOrDefault(iter => iter.SourceName == camera.SourceName) != null) {
                Console.WriteLine($"{camera.SourceName} is already in this scene");
                return;
            }

            SceneItemTransformInfo transform = new();
            transform.Alignnment = 5; // top left

            if (position == "main") {
                transform.X = 0;
                transform.Y = 0;
                transform.BoundsWidth = 1920;
                transform.BoundsHeight = 1080;
            } else if (position == "topright") {
                transform.BoundsWidth = 736;
                transform.BoundsHeight = 414;
                transform.X = 1920 - transform.BoundsWidth - 32;
                transform.Y = 1080 - transform.BoundsHeight - 32 - 480;
            } else if (position == "bottomright") {
                transform.BoundsWidth = 736;
                transform.BoundsHeight = 414;
                transform.X = 1920 - transform.BoundsWidth - 32;
                transform.Y = 1080 - transform.BoundsHeight - 32;
            } else {
                Console.WriteLine($"unchecked position '{position}'");
                return;
            }

            socket.DuplicateSceneItem("stream sources", sourceId, this.previewScene);

            List<SceneItemDetails> sources = socket.GetSceneItemList(this.previewScene);
            SceneItemDetails? newCamera = null;
            foreach (SceneItemDetails source in sources) {
                if (source.SourceName == camera.SourceName) {
                    Console.WriteLine($"Found source '{camera.SourceName}' at ID {source.ItemId}");
                    newCamera = source;
                    break;
                }
            }

            if (newCamera == null) {
                Console.Error.WriteLine($"failed to find {camera.SourceName} after duplicating it");
                return;
            }

            SidecamMeta meta = new();
            meta.ItemId = newCamera.ItemId;
            meta.Position = position;
            meta.Name = newCamera.SourceName;
            this.sideCams.Add(meta);
            this.UpdateSidecamUI();

            socket.SetSceneItemTransform(this.previewScene, newCamera.ItemId, JObject.FromObject(transform));
            //socket.SetSceneItemIndex(this.previewScene, newCamera.ItemId, -newCamera.ItemId);

            socket.SetInputMute(newCamera.SourceName, true);
        }

        private void Main_Click(object sender, RoutedEventArgs e) {
            int sourceId = (int)((Button)e.Source).Tag;
            CompositeCamera(sourceId, "main");
        }

        private void SubBottomRight_Click(object sender, RoutedEventArgs e) {
            int sourceId = (int)((Button)e.Source).Tag;
            CompositeCamera(sourceId, "bottomright");
        }

        private void SubTopRight_Click(object sender, RoutedEventArgs e) {
            int sourceId = (int)((Button)e.Source).Tag;
            CompositeCamera(sourceId, "topright");
        }

        private void ClearComposite_Click(object sender, RoutedEventArgs e) {
            ClearSidecams();
        }

        private void ClearSidecams() {
            if (this.socket.IsConnected == false) {
                return;
            }

            foreach (SidecamMeta meta in this.sideCams) {
                this.socket.RemoveSceneItem(this.previewScene, meta.ItemId);
            }
            this.sideCams.Clear();
            this.UpdateSidecamUI();
        }

        private void UpdateSidecamUI() {
            ActiveCamList.Dispatcher.BeginInvoke(() => {
                ActiveCamList.Items.Clear();
                foreach (SidecamMeta cam in this.sideCams) {
                    ActiveCamList.Items.Add(cam);
                }
            });
        }

    }
}
