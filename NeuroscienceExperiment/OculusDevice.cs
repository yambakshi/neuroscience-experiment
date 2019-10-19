using System;
using Axiom.Math;
using RiftDotNet;

namespace NeuroscienceExperiment
{
    public class OculusDevice
    {
        public IHMD m_hmd;
        private HMDManager m_hmdManager;
        public uint HorizontalResolution { get; private set; }
        public uint VerticalResolution { get; private set; }
        public float VScreenSize { get; private set; }
        public float EyeToScreenDistance { get; private set; }
        public float HScreenSize { get; private set; }
        public float LensSeparationDistance { get; private set; }
        public float InterpupillaryDistance { get; private set; }
        public float[] DistortionK { get; private set; }

        public void Initialize()
        {
            m_hmdManager = new HMDManager();
            m_hmd = m_hmdManager.AttachedDevice ?? m_hmdManager.WaitForAttachedDevice(null);

            HorizontalResolution = m_hmd.Info.HResolution;
            VerticalResolution = m_hmd.Info.VResolution;
            VScreenSize = m_hmd.Info.VScreenSize;
            EyeToScreenDistance = m_hmd.Info.EyeToScreenDistance;
            HScreenSize = m_hmd.Info.HScreenSize;
            LensSeparationDistance = m_hmd.Info.LensSeparationDistance;
            InterpupillaryDistance = m_hmd.Info.InterpupillaryDistance;
            DistortionK = m_hmd.Info.DistortionK;
            m_hmd.Reset();
        }
    }
}
