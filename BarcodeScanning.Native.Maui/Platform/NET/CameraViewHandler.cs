﻿namespace BarcodeScanning;

public partial class CameraViewHandler
{
    protected override BarcodeView CreatePlatformView() => throw new NotImplementedException();
    private void UpdateCamera() => throw new NotImplementedException();
    private void UpdateResolution() => throw new NotImplementedException();
    private void UpdateAnalyzer() => throw new NotImplementedException();
    private void UpdateTorch() => throw new NotImplementedException();
    private void HandleCameraEnabled() => throw new NotImplementedException();
    private void HandleAimModeEnabled() => throw new NotImplementedException();
    private void DisposeView() => throw new NotImplementedException();

    internal void Current_MainDisplayInfoChanged(object sender, DisplayInfoChangedEventArgs e) => throw new NotImplementedException();
}
