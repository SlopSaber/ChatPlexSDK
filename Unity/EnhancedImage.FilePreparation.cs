using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CP_SDK.Unity
{
    public partial class EnhancedImage
    {
        private sealed class PreparedFileImage
        {
            internal Animation.AnimationInfo Animation;
            internal Animation.ParticleAnimationPlan ParticlePlan;
            internal Color32[] Colors;
            internal int Width;
            internal int Height;
            internal bool Mirror;
            internal Exception Error;
        }

        /// <summary>Load an owned file request; advance this coroutine on the main thread.</summary>
        public static IEnumerator FromFileRetained(string p_FileName, string p_ID,
            Action<EnhancedImage> p_Callback, CancellationToken p_Cancellation)
        {
            var l_Loading = FromPreparedRetained(PrepareOwnedFile(p_FileName, p_Cancellation),
                p_FileName, p_ID, p_Callback, p_Cancellation, true);
            try
            {
                while (l_Loading.MoveNext())
                    yield return l_Loading.Current;
            }
            finally
            {
                (l_Loading as IDisposable)?.Dispose();
            }
        }

        internal static IEnumerator FromResourceRetained(Assembly p_Assembly, string p_Resource,
            string p_ID, Action<EnhancedImage> p_Callback, CancellationToken p_Cancellation)
        {
            // Keep resource-read completion and failures ahead of native startup.
            var l_Bytes = Task.Run(() =>
            {
                using (var l_Stream = p_Assembly.GetManifestResourceStream(p_Assembly.GetName().Name + "." + p_Resource))
                {
                    var l_Data = new byte[l_Stream.Length];
                    l_Stream.Read(l_Data, 0, (int)l_Stream.Length);
                    return l_Data;
                }
            }).GetAwaiter().GetResult();
            return FromPreparedRetained(PrepareOwnedResource(l_Bytes, p_Cancellation),
                p_Resource, p_ID, p_Callback, p_Cancellation, false);
        }

        private static IEnumerator FromPreparedRetained(Task<PreparedFileImage> p_Preparation,
            string p_Name, string p_ID, Action<EnhancedImage> p_Callback,
            CancellationToken p_Cancellation, bool p_UpdateParticlePlan)
        {
            var l_Preparation = p_Preparation;
            while (!l_Preparation.IsCompleted && !p_Cancellation.IsCancellationRequested)
                yield return null;

            if (p_Cancellation.IsCancellationRequested)
                yield break;

            var l_Prepared = l_Preparation.Result;
            if (l_Prepared.Error != null)
            {
                ChatPlexSDK.Logger.Error("[CP_SDK.Unity][EnhancedImage.FromFileRetained] Failed to prepare image " + p_Name);
                ChatPlexSDK.Logger.Error(l_Prepared.Error);
                p_Callback?.Invoke(null);
                yield break;
            }

            if (l_Prepared.Animation != null)
            {
                bool l_Completed = false;
                var l_Packing = Animation.AnimationLoader.CreateOwnedPackingCoroutine(l_Prepared.Animation,
                    (p_Texture, p_UVs, p_Delays, p_Width, p_Height) =>
                    {
                        l_Completed = true;
                        if (p_Cancellation.IsCancellationRequested)
                        {
                            if (p_Texture != null)
                                UnityEngine.Object.Destroy(p_Texture);
                            return;
                        }
                        OnRawAnimatedCallback(p_ID, p_Texture, p_UVs, p_Delays, p_Width, p_Height, p_Image =>
                        {
                            if (p_UpdateParticlePlan)
                                p_Image?.AnimControllerData?.SetParticlePlan(l_Prepared.ParticlePlan);
                            p_Callback?.Invoke(p_Image);
                        });
                    });
                try
                {
                    while (!p_Cancellation.IsCancellationRequested && l_Packing.MoveNext())
                        yield return l_Packing.Current;
                }
                finally
                {
                    (l_Packing as IDisposable)?.Dispose();
                }

                if (!l_Completed && !p_Cancellation.IsCancellationRequested)
                    p_Callback?.Invoke(null);
                yield break;
            }

            Texture2D l_Texture = null;
            Sprite l_Sprite = null;
            EnhancedImage l_Image = null;
            Exception l_Error = null;
            try
            {
                if (l_Prepared.Colors != null)
                {
                    l_Texture = new Texture2D(l_Prepared.Width, l_Prepared.Height, TextureFormat.RGBA32, false);
                    l_Texture.wrapMode = TextureWrapMode.Clamp;
                    l_Texture.SetPixels32(l_Prepared.Colors);
                    l_Texture.Apply(true);
                    l_Sprite = SpriteU.CreateFromTexture(l_Texture);
                    OnRawStaticCallback(p_ID, l_Sprite, p_Result => l_Image = p_Result);
                    if (l_Image != null && l_Prepared.Mirror)
                        l_Image.Sprite.texture.wrapMode = TextureWrapMode.Mirror;
                }
            }
            catch (Exception l_Exception)
            {
                l_Error = l_Exception;
                l_Image = null;
            }

            if (l_Image == null || p_Callback == null)
            {
                if (l_Sprite != null)
                    UnityEngine.Object.Destroy(l_Sprite);
                if (l_Texture != null)
                    UnityEngine.Object.Destroy(l_Texture);
            }
            if (l_Error != null)
            {
                ChatPlexSDK.Logger.Error("[CP_SDK.Unity][EnhancedImage.FromFileRetained] Failed to create image " + p_Name);
                ChatPlexSDK.Logger.Error(l_Error);
            }
            p_Callback?.Invoke(l_Image);
        }

        public static IEnumerator PrepareParticleAnimation(EnhancedImage p_Image, CancellationToken p_Cancellation)
        {
            if (p_Cancellation.IsCancellationRequested)
                yield break;
            var l_Controller = p_Image?.AnimControllerData;
            if (l_Controller == null || l_Controller.HasParticlePlan() || l_Controller.Frames == null
                || l_Controller.Delays == null || l_Controller.Frames.Length <= 1
                || l_Controller.Delays.Length < l_Controller.Frames.Length)
                yield break;

            var l_Count = l_Controller.Frames.Length;
            var l_Delays = new ushort[l_Count];
            Array.Copy(l_Controller.Delays, l_Delays, l_Count);
            var l_Plan = PrepareOwnedParticlePlan(l_Delays, l_Count);
            while (!l_Plan.IsCompleted && !p_Cancellation.IsCancellationRequested)
                yield return null;
            if (p_Cancellation.IsCancellationRequested)
                yield break;
            if (l_Plan.IsFaulted)
            {
                ChatPlexSDK.Logger.Error(l_Plan.Exception.GetBaseException());
                yield break;
            }
            if (ReferenceEquals(p_Image.AnimControllerData, l_Controller))
                l_Controller.SetParticlePlan(l_Plan.Result);
        }

        private static Task<Animation.ParticleAnimationPlan> PrepareOwnedParticlePlan(ushort[] p_Delays, int p_Count)
            => MTThreadInvoker.EnqueueRetained(() => Animation.ParticleAnimationPlan.Create(p_Delays, p_Count));

        private static async Task<PreparedFileImage> PrepareOwnedResource(byte[] p_Bytes, CancellationToken p_Cancellation)
        {
            var l_Result = new PreparedFileImage();
            try
            {
                if (!p_Cancellation.IsCancellationRequested)
                    await Animation.WEBP.WEBPDecoder.ProcessRetained(p_Bytes,
                        p_Info => l_Result.Animation = p_Info,
                        (p_Colors, p_Width, p_Height) =>
                        {
                            l_Result.Colors = p_Colors;
                            l_Result.Width = p_Width;
                            l_Result.Height = p_Height;
                        }).ConfigureAwait(false);
            }
            catch (Exception l_Exception)
            {
                l_Result.Error = l_Exception;
            }
            return l_Result;
        }

        private static Task<PreparedFileImage> PrepareOwnedFile(string p_FileName, CancellationToken p_Cancellation)
        {
            var l_Completion = new TaskCompletionSource<PreparedFileImage>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!MTThreadInvoker.TryEnqueueOnThread(() =>
            {
                var l_Result = new PreparedFileImage();
                try
                {
                    if (!p_Cancellation.IsCancellationRequested)
                    {
                        var l_Name = p_FileName.ToLower();
                        var l_Bytes = File.ReadAllBytes(p_FileName);
                        if (l_Name.EndsWith(".apng"))
                            Animation.APNG.APNGUnityDecoder.ProcessRetained(l_Bytes, p_Info => l_Result.Animation = p_Info).GetAwaiter().GetResult();
                        else if (l_Name.EndsWith(".gif") && ContainBytePattern(l_Bytes, ANIMATED_GIF_PATTERN))
                            Animation.GIF.GIFDecoder.ProcessRetained(l_Bytes, p_Info => l_Result.Animation = p_Info).GetAwaiter().GetResult();
                        else if (l_Name.EndsWith(".gif") && ContainBytePattern(l_Bytes, WEBPVP8_PATTERN))
                            Animation.WEBP.WEBPDecoder.ProcessRetained(l_Bytes, p_Info => l_Result.Animation = p_Info,
                                (p_Colors, p_Width, p_Height) =>
                                {
                                    l_Result.Colors = p_Colors;
                                    l_Result.Width = p_Width;
                                    l_Result.Height = p_Height;
                                }).GetAwaiter().GetResult();
                        else if (l_Name.EndsWith(".png") || l_Name.EndsWith(".gif"))
                        {
                            l_Result.Mirror = l_Name.EndsWith(".png");
                            using (var l_Stream = new MemoryStream(l_Bytes))
                            using (var l_DrawImage = System.Drawing.Image.FromStream(l_Stream))
                            using (var l_Bitmap = new System.Drawing.Bitmap(l_DrawImage))
                            {
                                l_Result.Width = l_Bitmap.Width;
                                l_Result.Height = l_Bitmap.Height;
                                l_Result.Colors = new Color32[l_Result.Width * l_Result.Height];
                                for (var l_Y = 0; l_Y < l_Result.Height; l_Y++)
                                {
                                    for (var l_X = 0; l_X < l_Result.Width; l_X++)
                                    {
                                        var l_Color = l_Bitmap.GetPixel(l_X, l_Y);
                                        l_Result.Colors[(l_Result.Height - l_Y - 1) * l_Result.Width + l_X] = new Color32(l_Color.R, l_Color.G, l_Color.B, l_Color.A);
                                    }
                                }
                            }
                        }
                    }
                    if (!p_Cancellation.IsCancellationRequested && l_Result.Animation != null
                        && l_Result.Animation.Delays != null && l_Result.Animation.Frames != null)
                    {
                        var l_Count = l_Result.Animation.Frames.Length;
                        if (l_Count > 0 && l_Result.Animation.Delays.Length >= l_Count)
                        {
                            var l_Delays = new ushort[l_Count];
                            Array.Copy(l_Result.Animation.Delays, l_Delays, l_Count);
                            l_Result.ParticlePlan = Animation.ParticleAnimationPlan.Create(l_Delays, l_Count);
                        }
                    }
                }
                catch (Exception l_Exception)
                {
                    l_Result.Error = l_Exception;
                }
                finally
                {
                    l_Completion.TrySetResult(l_Result);
                }
            }, () => l_Completion.TrySetResult(new PreparedFileImage
            {
                Error = new InvalidOperationException("Image preparation worker stopped before queued work could start.")
            })))
                l_Completion.TrySetResult(new PreparedFileImage { Error = new InvalidOperationException("Image preparation worker is unavailable or full.") });

            return l_Completion.Task;
        }
    }
}
