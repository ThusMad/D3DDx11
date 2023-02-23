using DirectN;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System;

namespace D11ToD3D.Dx11;

public class Dx11ImageSource : D3DImage, IDisposable
{
    // - field -----------------------------------------------------------------------

    private static int _activeClients;
    private static IComObject<IDirect3D9Ex>? _d3DContext;
    private static IComObject<IDirect3DDevice9Ex>? _d3DDevice;

    private IComObject<IDirect3DTexture9>? _renderTarget;

    // - public methods --------------------------------------------------------------

    public Dx11ImageSource()
    {
        StartD3D();
        _activeClients++;
    }

    public void Dispose()
    {
        SetRenderTarget(null);

        _renderTarget?.Dispose();

        _activeClients--;
        EndD3D();
    }

    public void InvalidateD3DImage()
    {
        if (_renderTarget == null)
        {
            return;
        }

        Lock();
        base.AddDirtyRect(new Int32Rect(0, 0, PixelWidth, PixelHeight));
        Unlock();
    }

    public void SetRenderTarget(IComObject<ID3D11Texture2D1>? target)
    {
        if (_renderTarget != null)
        {
            _renderTarget = null;
            Lock();
            SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero, true);
            Unlock();
        }

        if (target == null)
        {
            return;
        }

        if (target.IsDisposed)
        {
            return;
        }

        var format = TranslateFormat(target);
        var handle = GetSharedHandle(target);

        if (!IsShareable(target))
        {
            throw new ArgumentException("Texture must be created with D3D11_BIND_SHADER_RESOURCE");
        }

        if (format == _D3DFORMAT.D3DFMT_UNKNOWN)
        {
            throw new ArgumentException("Texture format is not compatible with OpenSharedResource");
        }

        if (handle == IntPtr.Zero)
        {
            throw new ArgumentException("Invalid handle");
        }

        if (_d3DDevice == null)
        {
            throw new ArgumentNullException(nameof(_d3DDevice));
        }

        var description = target.GetDesc();
        _d3DDevice.Object.CreateTexture(description.Width, description.Height, 1, Constants.D3DUSAGE_RENDERTARGET,
            format, _D3DPOOL.D3DPOOL_DEFAULT, out var texture, handle).ThrowOnError(true);

        _renderTarget = new ComObject<IDirect3DTexture9>(texture);
        _renderTarget.Object.GetSurfaceLevel(0, out var surfaceObj).ThrowOnError(true);

        using (var surface = new ComObject<IDirect3DSurface9>(surfaceObj))
        {
            Lock();
            SetBackBuffer(D3DResourceType.IDirect3DSurface9, Marshal.GetIUnknownForObject(surface.Object), true);
            Unlock();
        }
    }

    private static void StartD3D()
    {
        if (_activeClients != 0)
        {
            return;
        }

        var presentParams = GetPresentParameters();
        const uint createFlags = Constants.D3DCREATE_HARDWARE_VERTEXPROCESSING | Constants.D3DCREATE_MULTITHREADED |
                                 Constants.D3DCREATE_FPU_PRESERVE | Constants.D3DCREATE_DISABLE_PRINTSCREEN;
        Functions.Direct3DCreate9Ex(Constants.D3D_SDK_VERSION, out var context).ThrowOnError();
        _d3DContext = new ComObject<IDirect3D9Ex>(context);

        _d3DContext.Object.CreateDeviceEx(0, _D3DDEVTYPE.D3DDEVTYPE_HAL, IntPtr.Zero, createFlags,
            ref presentParams, 0, out var d3DDevice).ThrowOnError(true);

        _d3DDevice = new ComObject<IDirect3DDevice9Ex>(d3DDevice);
    }

    private void EndD3D()
    {
        if (_activeClients != 0)
        {
            return;
        }

        _renderTarget?.Dispose();
        _d3DDevice?.Dispose();
        _d3DContext?.Dispose();
    }

    private static _D3DPRESENT_PARAMETERS_ GetPresentParameters()
    {
        var hwnd = NativeMethods.GetDesktopWindow();

        var presentParams = new _D3DPRESENT_PARAMETERS_
        {
            PresentationInterval = 0x80000000,
            Windowed = true,
            SwapEffect = _D3DSWAPEFFECT.D3DSWAPEFFECT_DISCARD,
            hDeviceWindow = hwnd
        };

        return presentParams;
    }

    private static IntPtr GetSharedHandle(IComObject<ID3D11Texture2D1> texture)
    {
        //texture.As<IDXGIResource1>(true).CreateSharedHandle(IntPtr.Zero,
        //         unchecked((uint)Constants.DXGI_SHARED_RESOURCE_READ) | Constants.DXGI_SHARED_RESOURCE_WRITE, null, out var handle)
        //    .ThrowOnError(true);
        texture.As<IDXGIResource1>(true).GetSharedHandle(out var handle).ThrowOnError(true);
        return handle;

    }

    private static _D3DFORMAT TranslateFormat(IComObject<ID3D11Texture2D1> texture)
    {

        var description = texture.GetDesc();
        switch (description.Format)
        {
            case DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM: return _D3DFORMAT.D3DFMT_A2B10G10R10;
            case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT: return _D3DFORMAT.D3DFMT_A16B16G16R16F;
            case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM: return _D3DFORMAT.D3DFMT_A8R8G8B8;
            default:
                return _D3DFORMAT.D3DFMT_UNKNOWN;
        }
    }

    private static bool IsShareable(IComObject<ID3D11Texture2D1> texture)
    {
        var description = texture.GetDesc();

        return (description.BindFlags & (uint)D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE) != 0;
    }
}