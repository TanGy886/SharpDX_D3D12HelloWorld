﻿using SharpDX.DXGI;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace D3D12HelloFont
{
    using SharpDX;
    using SharpDX.Direct3D12;
    using SharpDX.Windows;

    public class HelloFont : IDisposable
    {
        /// <summary>
        /// Initialise pipeline and assets
        /// </summary>
        /// <param name="form">The form</param>
        public void Initialize(RenderForm form)
        {
            LoadPipeline(form);
            LoadAssets();
        }

        private void LoadPipeline(RenderForm form)
        {
            int width = form.ClientSize.Width;
            int height = form.ClientSize.Height;

            viewport.Width = width;
            viewport.Height = height;
            viewport.MaxDepth = 1.0f;

            scissorRect.Right = width;
            scissorRect.Bottom = height;

#if DEBUG
            // Enable the D3D12 debug layer.
            {
                DebugInterface.Get().EnableDebugLayer();
            }
#endif
            device = new Device(null, SharpDX.Direct3D.FeatureLevel.Level_12_0);
            using (var factory = new Factory4())
            {

                // Describe and create the command queue.
                CommandQueueDescription queueDesc = new CommandQueueDescription(CommandListType.Direct);
                commandQueue = device.CreateCommandQueue(queueDesc);


                // Describe and create the swap chain.
                SwapChainDescription swapChainDesc = new SwapChainDescription()
                {
                    BufferCount = FrameCount,
                    ModeDescription = new ModeDescription(width, height, new Rational(60, 1), Format.R8G8B8A8_UNorm),
                    Usage = Usage.RenderTargetOutput,
                    SwapEffect = SwapEffect.FlipDiscard,
                    OutputHandle = form.Handle,
                    //Flags = SwapChainFlags.None,
                    SampleDescription = new SampleDescription(1, 0),
                    IsWindowed = true
                };

                SwapChain tempSwapChain = new SwapChain(factory, commandQueue, swapChainDesc);
                swapChain = tempSwapChain.QueryInterface<SwapChain3>();
                tempSwapChain.Dispose();
                frameIndex = swapChain.CurrentBackBufferIndex;
            }

            // Create descriptor heaps.
            // Describe and create a render target view (RTV) descriptor heap.
            DescriptorHeapDescription rtvHeapDesc = new DescriptorHeapDescription()
            {
                DescriptorCount = FrameCount,
                Flags = DescriptorHeapFlags.None,
                Type = DescriptorHeapType.RenderTargetView
            };

            renderTargetViewHeap = device.CreateDescriptorHeap(rtvHeapDesc);

            rtvDescriptorSize = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);


            //Init Direct3D11 device from Direct3D12 device
            device11 = SharpDX.Direct3D11.Device.CreateFromDirect3D12(device, SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport, null, null, commandQueue);
            deviceContext11 = device11.ImmediateContext;
            device11on12 = device11.QueryInterface<SharpDX.Direct3D11.ID3D11On12Device>();
            var d2dFactory = new SharpDX.Direct2D1.Factory(SharpDX.Direct2D1.FactoryType.MultiThreaded);

            // Create frame resources.
            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart;
            for (int n = 0; n < FrameCount; n++)
            {
                renderTargets[n] = swapChain.GetBackBuffer<Resource>(n);
                device.CreateRenderTargetView(renderTargets[n], null, rtvHandle);
                rtvHandle += rtvDescriptorSize;

                //init Direct2D surfaces
                SharpDX.Direct3D11.D3D11ResourceFlags format = new SharpDX.Direct3D11.D3D11ResourceFlags()
                {
                    BindFlags = (int)SharpDX.Direct3D11.BindFlags.RenderTarget,
                    CPUAccessFlags = (int)SharpDX.Direct3D11.CpuAccessFlags.None
                };

                device11on12.CreateWrappedResource(
                    renderTargets[n], format,
                    (int)ResourceStates.Present,
                    (int)ResourceStates.RenderTarget,
                    typeof(SharpDX.Direct3D11.Resource).GUID,
                    out wrappedBackBuffers[n]);

                //Init direct2D surface
                var d2dSurface = wrappedBackBuffers[n].QueryInterface<Surface>();
                direct2DRenderTarget[n] = new SharpDX.Direct2D1.RenderTarget(d2dFactory, d2dSurface, new SharpDX.Direct2D1.RenderTargetProperties(new SharpDX.Direct2D1.PixelFormat(Format.Unknown, SharpDX.Direct2D1.AlphaMode.Premultiplied)));
                d2dSurface.Dispose();
            }

            commandAllocator = device.CreateCommandAllocator(CommandListType.Direct);

            d2dFactory.Dispose();

            //Init font
            var directWriteFactory = new SharpDX.DirectWrite.Factory();
            textFormat = new SharpDX.DirectWrite.TextFormat(directWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 48) { TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading, ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near };
            textBrush = new SharpDX.Direct2D1.SolidColorBrush(direct2DRenderTarget[0], Color.White);
            directWriteFactory.Dispose();
        }



        private void LoadAssets()
        {
            // Create an empty root signature.
            RootSignatureDescription rootSignatureDesc = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout);
            rootSignature = device.CreateRootSignature(rootSignatureDesc.Serialize());

            // Create the pipeline state, which includes compiling and loading shaders.

#if DEBUG
            var vertexShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "VSMain", "vs_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
#else
            var vertexShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "VSMain", "vs_5_0"));
#endif

#if DEBUG
            var pixelShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "PSMain", "ps_5_0", SharpDX.D3DCompiler.ShaderFlags.Debug));
#else
            var pixelShader = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders.hlsl", "PSMain", "ps_5_0"));
#endif

            // Define the vertex input layout.
            InputElement[] inputElementDescs = new InputElement[]
            {
                    new InputElement("POSITION",0,Format.R32G32B32_Float,0,0),
                    new InputElement("COLOR",0,Format.R32G32B32A32_Float,12,0)
            };

            // Describe and create the graphics pipeline state object (PSO).
            GraphicsPipelineStateDescription psoDesc = new GraphicsPipelineStateDescription()
            {
                InputLayout = new InputLayoutDescription(inputElementDescs),
                RootSignature = rootSignature,
                VertexShader = vertexShader,
                PixelShader = pixelShader,
                RasterizerState = RasterizerStateDescription.Default(),
                BlendState = BlendStateDescription.Default(),
                DepthStencilFormat = SharpDX.DXGI.Format.D32_Float,
                DepthStencilState = new DepthStencilStateDescription() { IsDepthEnabled = false, IsStencilEnabled = false },
                SampleMask = int.MaxValue,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetCount = 1,
                Flags = PipelineStateFlags.None,
                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                StreamOutput = new StreamOutputDescription()
            };
            psoDesc.RenderTargetFormats[0] = SharpDX.DXGI.Format.R8G8B8A8_UNorm;

            pipelineState = device.CreateGraphicsPipelineState(psoDesc);

            // Create the command list.
            commandList = device.CreateCommandList(CommandListType.Direct, commandAllocator, pipelineState);

            // Create the vertex buffer.
            float aspectRatio = viewport.Width / viewport.Height;

            // Define the geometry for a triangle.
            Vertex[] triangleVertices = new Vertex[]
            {
                    new Vertex() {position=new Vector3(0.0f, 0.25f * aspectRatio, 0.0f ),color=new Vector4(1.0f, 0.0f, 0.0f, 1.0f ) },
                    new Vertex() {position=new Vector3(0.25f, -0.25f * aspectRatio, 0.0f),color=new Vector4(0.0f, 1.0f, 0.0f, 1.0f) },
                    new Vertex() {position=new Vector3(-0.25f, -0.25f * aspectRatio, 0.0f),color=new Vector4(0.0f, 0.0f, 1.0f, 1.0f ) },
            };

            int vertexBufferSize = Utilities.SizeOf(triangleVertices);

            // Note: using upload heaps to transfer static data like vert buffers is not 
            // recommended. Every time the GPU needs it, the upload heap will be marshalled 
            // over. Please read up on Default Heap usage. An upload heap is used here for 
            // code simplicity and because there are very few verts to actually transfer.
            vertexBuffer = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(vertexBufferSize), ResourceStates.GenericRead);

            // Copy the triangle data to the vertex buffer.
            IntPtr pVertexDataBegin = vertexBuffer.Map(0);
            Utilities.Write(pVertexDataBegin, triangleVertices, 0, triangleVertices.Length);
            vertexBuffer.Unmap(0);

            // Initialize the vertex buffer view.
            vertexBufferView = new VertexBufferView();
            vertexBufferView.BufferLocation = vertexBuffer.GPUVirtualAddress;
            vertexBufferView.StrideInBytes = Utilities.SizeOf<Vertex>();
            vertexBufferView.SizeInBytes = vertexBufferSize;

            // Command lists are created in the recording state, but there is nothing
            // to record yet. The main loop expects it to be closed, so close it now.
            commandList.Close();

            // Create synchronization objects.
            fence = device.CreateFence(0, FenceFlags.None);
            fenceValue = 1;

            // Create an event handle to use for frame synchronization.
            fenceEvent = new AutoResetEvent(false);
        }

        private void PopulateCommandList()
        {
            // Command list allocators can only be reset when the associated 
            // command lists have finished execution on the GPU; apps should use 
            // fences to determine GPU execution progress.
            commandAllocator.Reset();

            // However, when ExecuteCommandList() is called on a particular command 
            // list, that command list can then be reset at any time and must be before 
            // re-recording.
            commandList.Reset(commandAllocator, pipelineState);


            // Set necessary state.
            commandList.SetGraphicsRootSignature(rootSignature);
            commandList.SetViewport(viewport);
            commandList.SetScissorRectangles(scissorRect);

            // Indicate that the back buffer will be used as a render target.
            commandList.ResourceBarrierTransition(renderTargets[frameIndex], ResourceStates.Present, ResourceStates.RenderTarget);


            CpuDescriptorHandle rtvHandle = renderTargetViewHeap.CPUDescriptorHandleForHeapStart;
            rtvHandle += frameIndex * rtvDescriptorSize;
            commandList.SetRenderTargets(1, rtvHandle, null);

            // Record commands.
            commandList.ClearRenderTargetView(rtvHandle, new Color4(0, 0.2F, 0.4f, 1), 0, null);

            commandList.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            commandList.SetVertexBuffer(0, vertexBufferView);
            commandList.DrawInstanced(3, 1, 0, 0);

            // Indicate that the back buffer will now be used to present.
            commandList.ResourceBarrierTransition(renderTargets[frameIndex], ResourceStates.RenderTarget, ResourceStates.Present);

            commandList.Close();
        }


        /// <summary> 
        /// Wait the previous command list to finish executing. 
        /// </summary> 
        private void WaitForPreviousFrame()
        {
            // WAITING FOR THE FRAME TO COMPLETE BEFORE CONTINUING IS NOT BEST PRACTICE. 
            // This is code implemented as such for simplicity. 

            int currentFence = fenceValue;
            commandQueue.Signal(fence, currentFence);
            fenceValue++;

            // Wait until the previous frame is finished.
            if (fence.CompletedValue < currentFence)
            {
                fence.SetEventOnCompletion(currentFence, fenceEvent.SafeWaitHandle.DangerousGetHandle());
                fenceEvent.WaitOne();
            }

            frameIndex = swapChain.CurrentBackBufferIndex;
        }

        public void Update()
        {
        }


        public void Render()
        {
            // Record all the commands we need to render the scene into the command list.
            PopulateCommandList();

            // Execute the command list.
            commandQueue.ExecuteCommandList(commandList);
            

            // Acquire our wrapped render target resource for the current back buffer.
            device11on12.AcquireWrappedResources(new SharpDX.Direct3D11.Resource[] { wrappedBackBuffers[frameIndex] }, 1);

            direct2DRenderTarget[frameIndex].BeginDraw();
            int time = Environment.TickCount % 10000;
            int t = time / 2000;
            float f = (time - (t * 2000)) / 2000.0F;
            textBrush.Color = Color4.Lerp(colors[t], colors[t + 1], f);
            direct2DRenderTarget[frameIndex].DrawText("Hello Text", textFormat, new SharpDX.Mathematics.Interop.RawRectangleF((float)Math.Sin(Environment.TickCount / 1000.0F) * 200 + 400, 10, 2000, 500), textBrush);
            direct2DRenderTarget[frameIndex].EndDraw();

            //Release wrapped render target resource
            device11on12.ReleaseWrappedResources(new SharpDX.Direct3D11.Resource[] { wrappedBackBuffers[frameIndex] }, 1);

            // Flush to submit the 11 command list to the shared command queue.
            deviceContext11.Flush();


            // Present the frame.
            swapChain.Present(1, 0);

            WaitForPreviousFrame();
        }


        public void Dispose()
        {
            // Wait for the GPU to be done with all resources.
            WaitForPreviousFrame();

            foreach (var wrappedBuffer in wrappedBackBuffers)
            {
                wrappedBuffer.Dispose();
            }

            foreach (var target in direct2DRenderTarget)
            {
                target.Dispose();
            }
            textFormat.Dispose();
            textBrush.Dispose();
            device11on12.Dispose();
            deviceContext11.Dispose();
            device11.Dispose();


            foreach (var target in renderTargets)
            {
                target.Dispose();
            }
            commandAllocator.Dispose();
            commandQueue.Dispose();
            rootSignature.Dispose();
            renderTargetViewHeap.Dispose();
            pipelineState.Dispose();
            commandList.Dispose();
            vertexBuffer.Dispose();
            fence.Dispose();
            swapChain.Dispose();
            device.Dispose();
        }


        struct Vertex
        {
            public Vector3 position;
            public Vector4 color;
        };

        const int FrameCount = 2;

        private ViewportF viewport;
        private Rectangle scissorRect;
        // Pipeline objects.
        private SwapChain3 swapChain;
        private Device device;
        private Resource[] renderTargets = new Resource[FrameCount];
        private CommandAllocator commandAllocator;
        private CommandQueue commandQueue;
        private RootSignature rootSignature;
        private DescriptorHeap renderTargetViewHeap;
        private PipelineState pipelineState;
        private GraphicsCommandList commandList;
        private int rtvDescriptorSize;

        // App resources.
        Resource vertexBuffer;
        VertexBufferView vertexBufferView;


        // Synchronization objects.
        private int frameIndex;
        private AutoResetEvent fenceEvent;

        private Fence fence;
        private int fenceValue;

        //Direct3D11 
        SharpDX.Direct3D11.Device device11;
        SharpDX.Direct3D11.DeviceContext deviceContext11;
        SharpDX.Direct3D11.ID3D11On12Device device11on12;
        SharpDX.Direct3D11.Resource[] wrappedBackBuffers = new SharpDX.Direct3D11.Resource[FrameCount];

        //Direct2D
        SharpDX.DirectWrite.TextFormat textFormat;
        SharpDX.Direct2D1.SolidColorBrush textBrush;
        SharpDX.Direct2D1.RenderTarget[] direct2DRenderTarget = new SharpDX.Direct2D1.RenderTarget[FrameCount];


        Color[] colors = new Color[]
        {
            Color.Red,
            Color.Magenta,
            Color.Yellow,
            Color.YellowGreen,
            Color.Green,
            Color.Red
        };
    }
}
