using CP_SDK.XUI;
using System.Collections;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CP_SDK.UI.Views
{
    /// <summary>
    /// Settings right view
    /// </summary>
    internal sealed class SettingsRightView : ViewController<SettingsRightView>
    {
        private XUITabControl   m_TabControl;

        private XUIToggle       m_OBSTab_Enabled;
        private XUITextInput    m_OBSTab_Server;
        private XUITextInput    m_OBSTab_Password;
        private XUIText         m_OBSTab_Status;

        private XUIToggle       m_EmotesTab_BBTVEnabled;
        private XUIToggle       m_EmotesTab_FFZEnabled;
        private XUIToggle       m_EmotesTab_7TVEnabled;
        private XUIToggle       m_EmotesTab_EmojisEnabled;
        private XUIToggle       m_EmotesTab_ParseTemporaryChannels;

        private XUIToggle       m_MiscTab_EventSpecialsEnabled;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private bool m_PreventChanges = false;
        private int m_ExportEpoch;
        private static readonly object s_ExportLock = new object();
        private static Task<bool> s_ExportTask;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On view creation
        /// </summary>
        protected override sealed void OnViewCreation()
        {
            Templates.FullRectLayout(
                Templates.TitleBar("Other settings"),

                XUITabControl.Make(
                    ("OBS",     BuildOBSTab()),
                    ("Emotes",  BuildEmotesTab()),
                    ("Tools",   BuildToolsTab()),
                    ("Misc",    BuildMiscTab())
                )
                .Bind(ref m_TabControl)
            )
            .SetBackground(true, null, true)
            .BuildUI(transform);

            OnValueChanged();
        }
        /// <summary>
        /// On view deactivation
        /// </summary>
        protected override sealed void OnViewDeactivation()
        {
            ++m_ExportEpoch;
            CPConfig.Instance.Save();
            ChatPlexServiceConfig.Instance.Save();
            Chat.ChatModSettings.Instance.Save();
        }

        protected override sealed void OnViewDestruction()
        {
            ++m_ExportEpoch;
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Build OBS tab
        /// </summary>
        /// <returns></returns>
        private XUIVLayout BuildOBSTab()
        {
            var l_OBSConfig = OBS.OBSModSettings.Instance;
            return XUIVLayout.Make(
                XUIText.Make("Status: X")
                    .SetAlign(TMPro.TextAlignmentOptions.CaplineGeoAligned)
                    .SetColor(Color.yellow)
                    .Bind(ref m_OBSTab_Status),

                XUIHLayout.Make(
                    XUIVLayout.Make(
                        XUIText.Make("Enabled"),
                        XUIText.Make("Server"),
                        XUIText.Make("Password")
                    )
                    .ForEachDirect<XUIText>(x => x.SetAlign(TMPro.TextAlignmentOptions.CaplineLeft)),

                    XUIVLayout.Make(
                        XUIToggle.Make()
                            .SetValue(l_OBSConfig.Enabled)
                            .Bind(ref m_OBSTab_Enabled),
                        XUITextInput.Make("Server address")
                            .SetValue(l_OBSConfig.Server)
                            .Bind(ref m_OBSTab_Server),
                        XUITextInput.Make("Password")
                            .SetIsPassword(true)
                            .SetValue(l_OBSConfig.Password)
                            .Bind(ref m_OBSTab_Password)
                    )
                    .ForEachDirect<XUIToggle>(x => x.OnValueChanged(_ => OnValueChanged()))
                    .ForEachDirect<XUITextInput>(x => x.OnValueChanged(_ => OnValueChanged()))
                ),

                XUIVSpacer.Make(10f),

                XUIPrimaryButton.Make("Apply", OnOBSTabApplyButton)
                    .SetWidth(60f)
                    .SetHeight(8f)
            );
        }
        /// <summary>
        /// Build emotes tab
        /// </summary>
        /// <returns></returns>
        private XUIVLayout BuildEmotesTab()
        {
            var l_EmotesConfig = Chat.ChatModSettings.Instance.Emotes;

            return XUIVLayout.Make(
                XUIHLayout.Make(
                    XUIVLayout.Make(
                        XUIText.Make("Parse BBTV Emotes"),
                        XUIText.Make("Parse FFZ Emotes"),
                        XUIText.Make("Parse 7TV Emotes"),
                        XUIText.Make("Parse Emojis Emotes"),
                        XUIText.Make("Parse emotes from temporary channels")
                    )
                    .OnReady(x => x.HOrVLayoutGroup.childForceExpandWidth = true)
                    .ForEachDirect<XUIText>(x => x.SetAlign(TMPro.TextAlignmentOptions.CaplineLeft)),

                    XUIVLayout.Make(
                        XUIToggle.Make()
                            .SetValue(l_EmotesConfig.ParseBTTVEmotes)
                            .Bind(ref m_EmotesTab_BBTVEnabled),
                        XUIToggle.Make()
                            .SetValue(l_EmotesConfig.ParseFFZEmotes)
                            .Bind(ref m_EmotesTab_FFZEnabled),
                        XUIToggle.Make()
                            .SetValue(l_EmotesConfig.Parse7TVEmotes)
                            .Bind(ref m_EmotesTab_7TVEnabled),
                        XUIToggle.Make()
                            .SetValue(l_EmotesConfig.ParseEmojis)
                            .Bind(ref m_EmotesTab_EmojisEnabled),
                        XUIToggle.Make()
                            .SetValue(l_EmotesConfig.ParseTemporaryChannels)
                            .Bind(ref m_EmotesTab_ParseTemporaryChannels)
                    )
                    .ForEachDirect<XUIToggle>(x => x.OnValueChanged(_ => OnValueChanged()))
                ),

                XUIVSpacer.Make(5f),

                XUIPrimaryButton.Make("Apply / Recache emotes", OnEmotesTabApplyButton)
                    .SetWidth(60f)
                    .SetHeight(8f)
            );
        }
        /// <summary>
        /// Build tools tab
        /// </summary>
        /// <returns></returns>
        private XUIVLayout BuildToolsTab()
        {
            return XUIVLayout.Make(
                XUIVLayout.Make(
                    XUIPrimaryButton.Make("Export LIV to camera2", OnLIVToCamera2Button)
                )
                .SetWidth(60f)
                .SetPadding(0)
                .ForEachDirect<XUIPrimaryButton>(y =>
                {
                    y.SetHeight(8f);
                    y.OnReady((x) => x.CSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained);
                })
                .ForEachDirect<XUISecondaryButton>(y =>
                {
                    y.SetHeight(8f);
                    y.OnReady((x) => x.CSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained);
                }),

                XUIVSpacer.Make(50f)
            );
        }
        /// <summary>
        /// Build misc tab
        /// </summary>
        /// <returns></returns>
        private XUIVLayout BuildMiscTab()
        {
            var l_CPConfig = CPConfig.Instance;

            return XUIVLayout.Make(
                XUIHLayout.Make(
                    XUIVLayout.Make(
                        XUIText.Make("Event specials (Require game restart)")
                    )
                    .OnReady(x => x.HOrVLayoutGroup.childForceExpandWidth = true)
                    .ForEachDirect<XUIText>(x => x.SetAlign(TMPro.TextAlignmentOptions.CaplineLeft)),

                    XUIVLayout.Make(
                        XUIToggle.Make()
                            .SetValue(l_CPConfig.EventSpecials)
                            .Bind(ref m_MiscTab_EventSpecialsEnabled)
                    )
                    .ForEachDirect<XUIToggle>(x => x.OnValueChanged(_ => OnValueChanged()))
                )
            );
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On frame
        /// </summary>
        private void Update()
        {
            if (m_TabControl.Element.GetActiveTab() == 0)
            {
                var l_Status = OBS.Service.Status;
                var l_Text = "Status: ";

                switch (l_Status)
                {
                    case OBS.Service.EStatus.Disconnected:
                    case OBS.Service.EStatus.Connecting:
                        l_Text += "<color=blue>";
                        break;

                    case OBS.Service.EStatus.Authing:
                        l_Text += "<color=yellow>";
                        break;

                    case OBS.Service.EStatus.Connected:
                        l_Text += "<color=green>";
                        break;

                    case OBS.Service.EStatus.AuthRejected:
                        l_Text += "<color=red>";
                        break;
                }

                l_Text += l_Status;

                if (m_OBSTab_Status.Element.TMProUGUI.text != l_Text)
                    m_OBSTab_Status.SetText(l_Text);
            }
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On setting changed
        /// </summary>
        private void OnValueChanged()
        {
            if (m_PreventChanges)
                return;

            var l_OBSConfig = OBS.OBSModSettings.Instance;
            l_OBSConfig.Enabled     = m_OBSTab_Enabled.Element.GetValue();
            l_OBSConfig.Server      = m_OBSTab_Server.Element.GetValue();
            l_OBSConfig.Password    = m_OBSTab_Password.Element.GetValue();

            m_OBSTab_Server  .SetInteractable(l_OBSConfig.Enabled);
            m_OBSTab_Password.SetInteractable(l_OBSConfig.Enabled);

            var l_EmotesConfig = Chat.ChatModSettings.Instance.Emotes;
            l_EmotesConfig.ParseBTTVEmotes          = m_EmotesTab_BBTVEnabled.Element.GetValue();
            l_EmotesConfig.ParseFFZEmotes           = m_EmotesTab_FFZEnabled.Element.GetValue();
            l_EmotesConfig.Parse7TVEmotes           = m_EmotesTab_7TVEnabled.Element.GetValue();
            l_EmotesConfig.ParseEmojis              = m_EmotesTab_EmojisEnabled.Element.GetValue();
            l_EmotesConfig.ParseTemporaryChannels   = m_EmotesTab_ParseTemporaryChannels.Element.GetValue();

            var l_CPConfig = CPConfig.Instance;
            l_CPConfig.EventSpecials = m_MiscTab_EventSpecialsEnabled.Element.GetValue();
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On apply setting button
        /// </summary>
        private void OnOBSTabApplyButton()
            => OBS.Service.ApplyConf();

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On apply setting button
        /// </summary>
        private void OnEmotesTabApplyButton()
        {
            Chat.Service.RecacheEmotes();
            ShowMessageModal("OK!");
        }
        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On LIV to camera2 button
        /// </summary>
        private void OnLIVToCamera2Button()
        {
            var l_LIVCamera = Resources.FindObjectsOfTypeAll<Camera>().FirstOrDefault(x => x.name == "LIV Camera");
            if (!l_LIVCamera)
            {
                ShowMessageModal("LIV not found!");
                return;
            }

            try
            {
                var l_Position = l_LIVCamera.transform.position;
                var l_Rotation = l_LIVCamera.transform.eulerAngles;
                var l_Values = new float[] {
                    l_LIVCamera.fieldOfView,
                    l_Position.x, l_Position.y, l_Position.z,
                    l_Rotation.x, l_Rotation.y, l_Rotation.z
                };
                var l_Request = new LIVExportRequest(
                    System.IO.Path.GetFullPath("UserData/Camera2/Cameras/BSP_LIV.json"),
                    CultureInfo.ReadOnly((CultureInfo)CultureInfo.CurrentCulture.Clone()),
                    l_Values);
                var l_Task = QueueExport(l_Request);
                CP_SDK.Unity.MTCoroutineStarter.Start(Coroutine_ExportCompleted(l_Task, m_ExportEpoch));
            }
            catch (System.Exception)
            {
                ShowMessageModal("Error!");
            }
        }

        private static Task<bool> QueueExport(LIVExportRequest p_Request)
        {
            lock (s_ExportLock)
            {
                s_ExportTask = s_ExportTask == null
                    ? Task.Run(p_Request.Run)
                    : s_ExportTask.ContinueWith(_ => p_Request.Run(), CancellationToken.None,
                        TaskContinuationOptions.None, TaskScheduler.Default);
                return s_ExportTask;
            }
        }

        private IEnumerator Coroutine_ExportCompleted(Task<bool> p_Task, int p_Epoch)
        {
            while (!p_Task.IsCompleted)
                yield return null;

            lock (s_ExportLock)
            {
                if (object.ReferenceEquals(s_ExportTask, p_Task))
                    s_ExportTask = null;
            }

            if (!this || !object.ReferenceEquals(Instance, this)
                || m_ExportEpoch != p_Epoch || !CanBeUpdated)
                yield break;

            ShowMessageModal(p_Task.GetAwaiter().GetResult()
                ? "Camera \"BSP_LIV\" created in camera2!"
                : "Error!");
        }

        private sealed class LIVExportRequest
        {
            private readonly string m_Path;
            private readonly CultureInfo m_Culture;
            private readonly float[] m_Values;

            public LIVExportRequest(string p_Path, CultureInfo p_Culture, float[] p_Values)
            {
                m_Path = p_Path;
                m_Culture = p_Culture;
                m_Values = p_Values;
            }

            public bool Run()
            {
                try
                {
                    var l_Profile = @"
{
  ""type"": ""Positionable"",
  ""worldCamVisibility"": ""HiddenWhilePlaying"",
  ""previewScreenSize"": 1.0,
  ""FOV"": $$FOV$$,
  ""layer"": -998,
  ""renderScale"": 1,
  ""farZ"": 1000.0,
  ""targetPos"": {
    ""x"": $$POSX$$,
    ""y"": $$POSY$$,
    ""z"": $$POSZ$$
  },
  ""targetRot"": {
    ""x"": $$ROTX$$,
    ""y"": $$ROTY$$,
    ""z"": $$ROTZ$$
  }
}";
                    var l_Tokens = new string[] {
                        "$$FOV$$", "$$POSX$$", "$$POSY$$", "$$POSZ$$",
                        "$$ROTX$$", "$$ROTY$$", "$$ROTZ$$"
                    };
                    for (var l_I = 0; l_I < l_Tokens.Length; ++l_I)
                        l_Profile = l_Profile.Replace(l_Tokens[l_I],
                            m_Values[l_I].ToString(m_Culture).Replace(',', '.'));

                    System.IO.File.WriteAllText(m_Path, l_Profile, System.Text.Encoding.UTF8);
                    return true;
                }
                catch (System.Exception)
                {
                    return false;
                }
            }
        }
    }
}
