using UnityEngine;

namespace CP_SDK.Animation
{
    internal sealed class ParticleAnimationPlan
    {
        private readonly ushort[] m_Delays;
        private readonly AnimationCurve m_Curve;
        private readonly float m_Duration;

        private ParticleAnimationPlan(ushort[] p_Delays, AnimationCurve p_Curve, float p_Duration)
        {
            m_Delays = p_Delays;
            m_Curve = p_Curve;
            m_Duration = p_Duration;
        }

        internal static ParticleAnimationPlan Create(ushort[] p_OwnedDelays, int p_FrameCount)
        {
            if (p_OwnedDelays == null || p_FrameCount <= 0 || p_OwnedDelays.Length < p_FrameCount)
                return null;

            float l_Duration = 0.0f;
            for (int l_I = 0; l_I < p_FrameCount; ++l_I)
                l_Duration += p_OwnedDelays[l_I];

            if (l_Duration <= 0.0f)
                return null;

            var l_Curve = new AnimationCurve();
            float l_Time = 0.0f;
            float l_Percentage = 1.0f / (float)p_FrameCount;
            for (int l_I = 0; l_I < p_FrameCount; ++l_I)
            {
                l_Curve.AddKey(l_Time / l_Duration, ((float)l_I) * l_Percentage);
                l_Time += p_OwnedDelays[l_I];
            }
            l_Curve.AddKey(1.0f, 1.0f);
            return new ParticleAnimationPlan(p_OwnedDelays, l_Curve, l_Duration);
        }

        internal bool Matches(ushort[] p_Delays, int p_FrameCount)
        {
            if (p_Delays == null || p_FrameCount <= 0 || p_Delays.Length < p_FrameCount || m_Delays.Length != p_FrameCount)
                return false;
            for (int l_I = 0; l_I < p_FrameCount; ++l_I)
                if (p_Delays[l_I] != m_Delays[l_I])
                    return false;
            return true;
        }

        internal bool TryCopyTo(AnimationCurve p_Target, ushort[] p_Delays, int p_FrameCount, float p_Duration)
        {
            if (m_Duration != p_Duration || !Matches(p_Delays, p_FrameCount))
                return false;
            p_Target.CopyFrom(m_Curve);
            return true;
        }
    }
}
