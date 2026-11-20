// LanPin native desktop composition bridge. Part of LanPin, GPL-3.0.
#include <windows.h>
#include <dispatcherqueue.h>
#include <windows.ui.composition.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
#include <memory>
using namespace winrt;
using namespace Windows::UI::Composition;
namespace {
// Share one queue for the lifetime of the WPF UI thread, not one per window.
thread_local Windows::System::DispatcherQueueController uiQueue{nullptr};
void ensureQueue() {
    if (!Windows::System::DispatcherQueue::GetForCurrentThread()) {
        DispatcherQueueOptions options{sizeof(options), DQTYPE_THREAD_CURRENT, DQTAT_COM_NONE};
        check_hresult(CreateDispatcherQueueController(options,
            reinterpret_cast<ABI::Windows::System::IDispatcherQueueController**>(put_abi(uiQueue))));
    }
}
class GlassLayer {
    DWORD owner = GetCurrentThreadId();
    Compositor compositor{nullptr};
    Desktop::DesktopWindowTarget target{nullptr};
    SpriteVisual root{nullptr};
    CompositionRoundedRectangleGeometry shape{nullptr};
public:
    explicit GlassLayer(HWND hwnd) {
        if (!IsWindow(hwnd)) throw hresult_invalid_argument();
        ensureQueue();
        compositor = Compositor();
        auto desktop = compositor.as<ABI::Windows::UI::Composition::Desktop::ICompositorDesktopInterop>();
        check_hresult(desktop->CreateDesktopWindowTarget(hwnd, FALSE,
            reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(put_abi(target))));
        shape = compositor.CreateRoundedRectangleGeometry();
        root = compositor.CreateSpriteVisual();
        root.Brush(compositor.CreateHostBackdropBrush());
        root.Clip(compositor.CreateGeometricClip(shape));
        target.Root(root);
    }
    void update(float width,float height,float radius,bool visible) {
        if (GetCurrentThreadId()!=owner) throw hresult_wrong_thread();
        root.IsVisible(visible);
        root.Size({width,height});
        shape.Size({width,height});
        shape.CornerRadius({radius,radius});
    }
    ~GlassLayer() noexcept {
        try { if(target) target.Root(nullptr); } catch(...) {}
        shape=nullptr;root=nullptr;target=nullptr;compositor=nullptr;
    }
};
}
extern "C" __declspec(dllexport) HRESULT __stdcall LanPinCreateGlass(HWND hwnd,void** result) noexcept {
    if(!result)return E_POINTER;*result=nullptr;
    try {auto layer=std::make_unique<GlassLayer>(hwnd);*result=layer.release();return S_OK;}
    catch(...) {return to_hresult();}
}
extern "C" __declspec(dllexport) HRESULT __stdcall LanPinUpdateGlass(void* layer,float width,float height,float radius,int visible) noexcept {
    if(!layer||width<0||height<0||radius<0)return E_INVALIDARG;
    try {static_cast<GlassLayer*>(layer)->update(width,height,radius,visible!=0);return S_OK;}
    catch(...) {return to_hresult();}
}
extern "C" __declspec(dllexport) void __stdcall LanPinDestroyGlass(void* layer) noexcept {
    delete static_cast<GlassLayer*>(layer);
}
