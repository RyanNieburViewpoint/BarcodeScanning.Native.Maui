﻿using Android.Content;
using Android.Widget;
using AndroidX.Camera.Core;
using AndroidX.Camera.View;
using AndroidX.Lifecycle;
using Java.Util.Concurrent;
using static Android.Views.ViewGroup;

namespace BarcodeScanning;

public partial class CameraViewHandler
{
    private BarcodeAnalyzer _barcodeAnalyzer;
    private BarcodeView _barcodeView;
    private IExecutorService _cameraExecutor;
    private LifecycleCameraController _cameraController;
    private PreviewView _previewView;

    private readonly int _delay = 200;
    private bool _cameraRunning = false;

    protected override BarcodeView CreatePlatformView()
    {
        _cameraExecutor = Executors.NewSingleThreadExecutor();
        _cameraController = new LifecycleCameraController(Context)
        {
            TapToFocusEnabled = VirtualView?.TapToFocusEnabled ?? false,
            ImageAnalysisBackpressureStrategy = ImageAnalysis.StrategyKeepOnlyLatest
        };
        _cameraController.SetEnabledUseCases(CameraController.ImageAnalysis);
        _previewView = new PreviewView(Context)
        {
            Controller = _cameraController,
            LayoutParameters = new RelativeLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent)
        };
        _previewView.SetScaleType(PreviewView.ScaleType.FillCenter);
        _previewView.SetImplementationMode(PreviewView.ImplementationMode.Performance);
        _barcodeView = new BarcodeView(Context, _previewView);

        return _barcodeView;
    }

    internal void Start()
    { 
        if (_cameraController is not null)
        {
            _cameraController.Unbind();
            _cameraRunning = false;

            ILifecycleOwner lifecycleOwner = null;
            if (Context is ILifecycleOwner)
                lifecycleOwner = Context as ILifecycleOwner;
            else if ((Context as ContextWrapper)?.BaseContext is ILifecycleOwner)
                lifecycleOwner = (Context as ContextWrapper)?.BaseContext as ILifecycleOwner;
            else if (Platform.CurrentActivity is ILifecycleOwner)
                lifecycleOwner = Platform.CurrentActivity as ILifecycleOwner;

            if (lifecycleOwner is null)
                return;

            if (_cameraController.CameraSelector is null)
                UpdateCamera();
            if (_cameraController.ImageAnalysisTargetSize is null)
                UpdateResolution();

            UpdateAnalyzer();

            _cameraController.BindToLifecycle(lifecycleOwner);
            _cameraRunning = true;
        }
    }

    private void Stop()
    {
        if (_cameraController is not null)
        {
            _cameraController.EnableTorch(false);
            _cameraController.Unbind();
            _cameraRunning = false;
        }
    }

    private void HandleCameraEnabled()
    {
        //Delay to let transition animation finish
        //https://stackoverflow.com/a/67765792
        if (VirtualView?.CameraEnabled ?? false)
            _ = Task.Run(async () =>
            {
                await Task.Delay(_delay);
                MainThread.BeginInvokeOnMainThread(Start);
            });
        else
            _ = Task.Run(async () =>
            {
                await Task.Delay(_delay);
                MainThread.BeginInvokeOnMainThread(Stop);
            });
    }

    //TODO Implement camera-mlkit-vision
    //https://developer.android.com/reference/androidx/camera/mlkit/vision/MlKitAnalyzer
    private void UpdateAnalyzer()
    {
        if (_cameraController is not null && _cameraExecutor is not null && ((IViewHandler)this).VirtualView is not null && _previewView is not null)
        {
            _cameraController.ClearImageAnalysisAnalyzer();
            _barcodeAnalyzer?.Dispose();
            _barcodeAnalyzer = new BarcodeAnalyzer(VirtualView, _previewView, this);
            _cameraController.SetImageAnalysisAnalyzer(_cameraExecutor, _barcodeAnalyzer);
        }
    }

    private void UpdateCamera()
    {
        if (_cameraController is not null)
        {
            if (VirtualView?.CameraFacing == CameraFacing.Front)
                _cameraController.CameraSelector = CameraSelector.DefaultFrontCamera;
            else
                _cameraController.CameraSelector = CameraSelector.DefaultBackCamera;
        }

    }

    //TODO Implement setImageAnalysisResolutionSelector
    //https://developer.android.com/reference/androidx/camera/view/CameraController#setImageAnalysisResolutionSelector(androidx.camera.core.resolutionselector.ResolutionSelector)
    private void UpdateResolution()
    {
        if (_cameraController is not null)
            _cameraController.ImageAnalysisTargetSize = new CameraController.OutputSize(Methods.TargetResolution(VirtualView?.CaptureQuality));

        if (_cameraRunning)
            Start();
    }

    private void UpdateTorch()
    {
        if (_cameraController is not null)
            _cameraController.EnableTorch(VirtualView?.TorchOn ?? false);
    }

    private void HandleAimModeEnabled()
    {
        if (_barcodeView is not null && VirtualView is not null)
        {
            if (VirtualView.AimMode)
                _barcodeView.AddAimingDot();
            else
                _barcodeView.RemoveAimingDot();
        }
    }

    private void DisposeView()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(_delay);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                this.Stop();

                _barcodeView?.Dispose();
                _previewView?.Dispose();
                _cameraController?.Dispose();
                _barcodeAnalyzer?.Dispose();
                _cameraExecutor?.Dispose();
            });
        });
    }

    internal void Current_MainDisplayInfoChanged(object sender, DisplayInfoChangedEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(_delay);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (VirtualView.CameraEnabled)
                        UpdateResolution();
                }
                catch (Exception)
                {
                    DeviceDisplay.Current.MainDisplayInfoChanged -= Current_MainDisplayInfoChanged;
                }
            });
        });
    }
}