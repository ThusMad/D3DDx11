using DirectN;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows;
using System;
using System.Windows.Controls;

namespace D11ToD3D.Dx11;

public class DxRenderer : Image
{
    private IComObject<ID3D11Device4>? _device;
    private IComObject<ID3D11DeviceContext2>? _deviceContext;
    private IComObject<ID3D11Texture2D1>? _renderTarget;
    private Dx11ImageSource? _d3DSurface;
    private IComObject<ID2D1RenderTarget>? _d2DRenderTarget;
    private IComObject<ID2D1Factory>? _d2DFactory;

    private D3D11_TEXTURE2D_DESC _renderDesc;
    private D2D1_RENDER_TARGET_PROPERTIES _renderProperties;


    public static bool IsInDesignMode
    {
        get
        {
            var prop = DesignerProperties.IsInDesignModeProperty;
            var isDesignMode = (bool)DependencyPropertyDescriptor.FromProperty(prop, typeof(FrameworkElement)).Metadata
                .DefaultValue;
            return isDesignMode;
        }
    }

    // - public methods --------------------------------------------------------------

    public DxRenderer()
    {
        Loaded += Image_Loaded;
        Unloaded += Image_Unloaded;

        SnapsToDevicePixels = true;
        VisualEdgeMode = EdgeMode.Aliased;
        Stretch = Stretch.Fill;
    }

    private void Image_Loaded(object sender, RoutedEventArgs e)
    {
        if (IsInDesignMode)
        {
            return;
        }

        _renderDesc = new D3D11_TEXTURE2D_DESC
        {
            BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET |
                        (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            MipLevels = 1,
            SampleDesc = new DXGI_SAMPLE_DESC()
            {
                Count = 1,
                Quality = 0
            },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            MiscFlags = (uint)D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_SHARED,
            CPUAccessFlags = 0,
            ArraySize = 1,
        };

        _renderProperties = new D2D1_RENDER_TARGET_PROPERTIES()
        {
            dpiX = 96,
            dpiY = 96,
            pixelFormat = new D2D1_PIXEL_FORMAT()
            {
                alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED,
                format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN
            }
        };

        StartD3D();

        StartRendering();
    }

    private void Image_Unloaded(object sender, RoutedEventArgs e)
    {
        if (IsInDesignMode)
        {
            return;
        }

        EndD3D();
    }


    private void InvalidateImage()
    {
        _d3DSurface?.InvalidateD3DImage();
    }


    private void OnIsFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_d3DSurface is { IsFrontBufferAvailable: true })
        {
            StartRendering();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        CreateAndBindTargets();
        base.OnRenderSizeChanged(sizeInfo);

        _d2DRenderTarget.BeginDraw();
        _d2DRenderTarget.Clear(new _D3DCOLORVALUE(0,0,0));
        _d2DRenderTarget.EndDraw();

        _deviceContext?.Object.Flush();
    }

    // - private methods -------------------------------------------------------------

    private void StartRendering()
    {
        CreateAndBindTargets();
    }

    private void StartD3D()
    {
        var levels = new[]
        {
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0
        };

        D3D11Functions.D3D11CreateDevice(
            null,
            D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
            0,
            (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT |
            (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_DEBUG,
            levels, (uint)levels.Length,
            Constants.D3D11_SDK_VERSION,
            out var ppDevice,
            out _,
            out var context).ThrowOnError();

        _device = new ComObject<ID3D11Device5>(new ComObject<ID3D11Device>(ppDevice).As<ID3D11Device5>());
        _deviceContext =
            new ComObject<ID3D11DeviceContext2>(new ComObject<ID3D11DeviceContext>(context).As<ID3D11DeviceContext2>());

        _d3DSurface = new Dx11ImageSource();

        _d3DSurface.IsFrontBufferAvailableChanged += OnIsFrontBufferAvailableChanged;
        CreateAndBindTargets();

        Source = _d3DSurface;
    }

    private void EndD3D()
    {
        if (_d3DSurface != null)
            _d3DSurface.IsFrontBufferAvailableChanged -= OnIsFrontBufferAvailableChanged;

        Source = null;

        _d2DRenderTarget?.Dispose();
        _d2DFactory?.Dispose();
        _d3DSurface?.Dispose();
        _renderTarget?.Dispose();
        _device?.Dispose();
    }

    private void CreateAndBindTargets()
    {
        if (_d3DSurface == null)
        {
            return;
        }

        _d3DSurface.SetRenderTarget(null);

        _d2DRenderTarget?.Dispose();
        _d2DFactory?.Dispose();
        _renderTarget?.Dispose();

        var width = Math.Max((int)Math.Floor(ActualWidth), 100);
        var height = Math.Max((int)Math.Floor(ActualHeight), 100);

        _renderDesc.Width = (uint)width;
        _renderDesc.Height = (uint)height;


        _renderTarget = _device.CreateTexture2D<ID3D11Texture2D1>(_renderDesc, null);

        var surface = _renderTarget.As<IDXGISurface>(true);

        _d2DFactory = D2D1Functions.D2D1CreateFactory();
        _d2DFactory.Object.CreateDxgiSurfaceRenderTarget(surface, ref _renderProperties, out var target)
            .ThrowOnError(true);

        _d2DRenderTarget = new ComObject<ID2D1RenderTarget>(target);
        _d2DRenderTarget.Object.SetAntialiasMode(D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);

        _d3DSurface.SetRenderTarget(_renderTarget);
        _deviceContext.RSSetViewport(new D3D11_VIEWPORT()
        {
            Height = height,
            Width = width,
            TopLeftX = 0,
            TopLeftY = 0
        });
    }
}