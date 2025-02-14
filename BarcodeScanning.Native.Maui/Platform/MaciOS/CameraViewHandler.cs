﻿using AVFoundation;
using CoreFoundation;
using Foundation;
using UIKit;

namespace BarcodeScanning;

public partial class CameraViewHandler
{
    private AVCaptureVideoPreviewLayer _videoPreviewLayer;
    private AVCaptureVideoDataOutput _videoDataOutput;
    private AVCaptureSession _captureSession;
    private AVCaptureDevice _captureDevice;
    private AVCaptureInput _captureInput;
    private DispatchQueue _queue;
    private BarcodeAnalyzer _barcodeAnalyzer;
    private BarcodeView _barcodeView;
    private UITapGestureRecognizer _uITapGestureRecognizer;
    private NSObject _subjectAreaChangedNotificaion;

    private readonly object _syncLock = new();
    private readonly object _configLock = new();

    protected override BarcodeView CreatePlatformView()
    {
        _captureSession = new AVCaptureSession();
        _uITapGestureRecognizer = new UITapGestureRecognizer(FocusOnTap);
        _queue = new DispatchQueue("BarcodeScannerQueue", new DispatchQueue.Attributes()
        {
            QualityOfService = DispatchQualityOfService.UserInitiated
        });
        _videoPreviewLayer = new AVCaptureVideoPreviewLayer(_captureSession)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill
        };
        _barcodeView = new BarcodeView(_videoPreviewLayer);
        _barcodeView.AddGestureRecognizer(_uITapGestureRecognizer);
        _subjectAreaChangedNotificaion = NSNotificationCenter.DefaultCenter.AddObserver(AVCaptureDevice.SubjectAreaDidChangeNotification, (n) => 
        {
            if (n.Name == AVCaptureDevice.SubjectAreaDidChangeNotification)
                ResetFocus();
        });
        
        return _barcodeView;
    }

    internal void Start()
    {
        if (_captureSession is not null)
        {
            if (_captureSession.Running)
                _captureSession.StopRunning();
            
            if (_captureSession.Inputs.Length == 0)
                UpdateCamera();
            if (_captureSession.Outputs.Length == 0)
                UpdateAnalyzer();
            if (_captureSession.SessionPreset is null)
                UpdateResolution();

            lock (_syncLock)
            {
                _captureSession.StartRunning();
            }
        }
    }

    private void Stop()
    {
        if (_captureSession is not null)
        {
            if (_captureDevice is not null && _captureDevice.TorchActive)
                CaptureDeviceLock(() => _captureDevice.TorchMode = AVCaptureTorchMode.Off);

            if (_captureSession.Running)
                _captureSession.StopRunning();
        }
    }

    private void HandleCameraEnabled()
    {
        if (VirtualView?.CameraEnabled ?? false)
            Start();
        else
            Stop();
    }

    private void UpdateResolution()
    {
        if (_captureSession is not null)
        {
            var quality = VirtualView?.CaptureQuality ?? CaptureQuality.Medium;

            lock (_syncLock)
            {
                _captureSession.BeginConfiguration();

                while (!_captureSession.CanSetSessionPreset(GetCaptureSessionResolution(quality)) && quality != CaptureQuality.Low)
                {
                    quality -= 1;
                }
                _captureSession.SessionPreset = GetCaptureSessionResolution(quality);

                _captureSession.CommitConfiguration();
            }
        }
    }
    private void UpdateAnalyzer()
    {
        if (_captureSession is not null)
        {
            lock (_syncLock)
            {
                _captureSession.BeginConfiguration();

                if (_videoDataOutput is not null)
                {
                    if (_captureSession.Outputs.Length > 0 && _captureSession.Outputs.Contains(_videoDataOutput))
                        _captureSession.RemoveOutput(_videoDataOutput);

                    _videoDataOutput.Dispose();
                }

                _videoDataOutput = new AVCaptureVideoDataOutput()
                {
                    AlwaysDiscardsLateVideoFrames = true
                };
                
                if (VirtualView is not null && _videoPreviewLayer is not null && _queue is not null)
                {
                    _barcodeAnalyzer?.Dispose();
                    _barcodeAnalyzer = new BarcodeAnalyzer(VirtualView, _videoPreviewLayer, this);
                    _videoDataOutput.SetSampleBufferDelegate(_barcodeAnalyzer, _queue);
                }

                if (_captureSession.CanAddOutput(_videoDataOutput))
                    _captureSession.AddOutput(_videoDataOutput);

                _captureSession.CommitConfiguration();
            }
        }
    }
    private void UpdateCamera()
    {
        if (_captureSession is not null)
        {
            lock (_syncLock)
            {
                _captureSession.BeginConfiguration();

                _captureSession.SessionPreset = AVCaptureSession.Preset1280x720;

                if (_captureInput is not null)
                {
                    if (_captureSession.Inputs.Length > 0 && _captureSession.Inputs.Contains(_captureInput))
                        _captureSession.RemoveInput(_captureInput);

                    _captureInput.Dispose();
                }

                _captureDevice?.Dispose();
                if (VirtualView?.CameraFacing == CameraFacing.Front)
                {
                    _captureDevice = AVCaptureDevice.GetDefaultDevice(AVCaptureDeviceType.BuiltInWideAngleCamera, AVMediaTypes.Video, AVCaptureDevicePosition.Front);
                }
                else
                {
                    _captureDevice = AVCaptureDevice.GetDefaultDevice(AVCaptureDeviceType.BuiltInTripleCamera, AVMediaTypes.Video, AVCaptureDevicePosition.Back);
                    _captureDevice ??= AVCaptureDevice.GetDefaultDevice(AVCaptureDeviceType.BuiltInDualWideCamera, AVMediaTypes.Video, AVCaptureDevicePosition.Back);
                    _captureDevice ??= AVCaptureDevice.GetDefaultDevice(AVCaptureDeviceType.BuiltInDualCamera, AVMediaTypes.Video, AVCaptureDevicePosition.Back);
                    _captureDevice ??= AVCaptureDevice.GetDefaultDevice(AVCaptureDeviceType.BuiltInWideAngleCamera, AVMediaTypes.Video, AVCaptureDevicePosition.Back);
                }
                _captureDevice ??= AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);

                if (_captureDevice is not null)
                {
                    ResetFocus();
                    _captureInput = new AVCaptureDeviceInput(_captureDevice, out _);

                    if (_captureSession.CanAddInput(_captureInput))
                        _captureSession.AddInput(_captureInput);
                }

                _captureSession.CommitConfiguration();
            }

            UpdateResolution();
        }
    }
    
    private void UpdateTorch()
    {
        if (_captureDevice is not null && _captureDevice.HasTorch && _captureDevice.TorchAvailable)
        {
            if (VirtualView?.TorchOn ?? false)
                CaptureDeviceLock(() => 
                {
                    if(_captureDevice.IsTorchModeSupported(AVCaptureTorchMode.On))
                        _captureDevice.TorchMode = AVCaptureTorchMode.On;
                });
            else
                CaptureDeviceLock(() =>
                {
                    if(_captureDevice.IsTorchModeSupported(AVCaptureTorchMode.Off))
                        _captureDevice.TorchMode = AVCaptureTorchMode.Off;
                });
        }
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

    private void FocusOnTap()
    {
        if (_captureDevice is not null && (VirtualView?.TapToFocusEnabled ?? false) && _captureDevice.FocusPointOfInterestSupported)
        {
            CaptureDeviceLock(() =>
            {
                _captureDevice.FocusPointOfInterest = _videoPreviewLayer.CaptureDevicePointOfInterestForPoint(_uITapGestureRecognizer.LocationInView(_barcodeView));
                _captureDevice.FocusMode = AVCaptureFocusMode.AutoFocus;
                _captureDevice.SubjectAreaChangeMonitoringEnabled = true;
            });
        }
    }

    private void ResetFocus()
    {
        CaptureDeviceLock(() => 
        {
            if (_captureDevice.IsFocusModeSupported(AVCaptureFocusMode.ContinuousAutoFocus))
                _captureDevice.FocusMode = AVCaptureFocusMode.ContinuousAutoFocus;
            else
                _captureDevice.FocusMode = AVCaptureFocusMode.AutoFocus;
            
            _captureDevice.SubjectAreaChangeMonitoringEnabled = false;
        });
    }

    private static NSString GetCaptureSessionResolution(CaptureQuality quality)
    {
        return quality switch
        {
            CaptureQuality.Low => AVCaptureSession.Preset640x480,
            CaptureQuality.Medium => AVCaptureSession.Preset1280x720,
            CaptureQuality.High => AVCaptureSession.Preset1920x1080,
            CaptureQuality.Highest => AVCaptureSession.Preset3840x2160,
            _ => AVCaptureSession.Preset1280x720
        };
    }
    private void CaptureDeviceLock(Action handler)
    {
        MainThread.BeginInvokeOnMainThread(() => 
        {
            lock (_configLock)
            {
                if (_captureDevice?.LockForConfiguration(out _) ?? false)
                {
                    try
                    {
                        handler();
                    }
                    catch (Exception)
                    {
                            
                    }
                    finally
                    {
                        _captureDevice.UnlockForConfiguration();
                    }
                }
            }
        });
    }

    internal void Current_MainDisplayInfoChanged(object sender, DisplayInfoChangedEventArgs e)
    {
    }

    private void DisposeView()
    {
        this.Stop();

        _subjectAreaChangedNotificaion?.Dispose();
        _subjectAreaChangedNotificaion = null;
        _barcodeView?.Dispose();
        _barcodeView = null;
        _uITapGestureRecognizer?.Dispose();
        _uITapGestureRecognizer = null;
        _videoPreviewLayer?.Dispose();
        _videoPreviewLayer = null;
        _captureSession?.Dispose();
        _captureSession = null;
        _videoDataOutput?.Dispose();
        _videoDataOutput = null;
        _captureInput?.Dispose();
        _captureInput = null;
        _captureDevice?.Dispose();
        _captureDevice = null;
        _barcodeAnalyzer?.Dispose();
        _barcodeAnalyzer = null;
        _queue?.Dispose();
        _queue = null;
    }
}