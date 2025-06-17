using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using DxPathRendering.Extensions;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace DxPathRendering
{
    public unsafe class MeshRenderer : IDisposable
    {
        private static D3D11? _api;
        private static D3DCompiler? _compiler;
        private static volatile int _instanceCount;

        ComPtr<ID3D11Device> _device;
        ComPtr<ID3D11DeviceContext> _deviceContext;
        ComPtr<ID3D11Buffer> _vertexBuffer = default;
        ComPtr<ID3D11Buffer> _indexBuffer = default;
        ComPtr<ID3D11Texture2D> _renderTarget;
        ComPtr<ID3D11Texture2D> _outputBuffer;
        ComPtr<ID3D11Texture2D> _resolveTexture; // 用于MSAA解析的中间纹理
        ComPtr<ID3D10Blob> _vertexShaderCode;
        ComPtr<ID3D10Blob> _pixelShaderCode;
        ComPtr<ID3D11VertexShader> _vertexShader;
        ComPtr<ID3D11PixelShader> _pixelShader;
        ComPtr<ID3D11InputLayout> _inputLayout;
        ComPtr<ID3D11Buffer> _constantBuffer;

        private int _inputWidth;
        private int _inputHeight;
        private MatrixTransform _transform;
        private bool _transformSet;
        private bool _antialiasingEnabled; // 控制抗锯齿是否启用
        private int _msaaSampleCount; // MSAA采样数量

        public int OutputWidth { get; }
        public int OutputHeight { get; }

        public MeshRenderer(
            int outputWidth, int outputHeight)
        {
            _instanceCount++;
            OutputWidth = outputWidth;
            OutputHeight = outputHeight;
            // 默认为单位矩阵
            _transform = new MatrixTransform(1, 0, 0, 1, 0, 0);
            _transformSet = false;
            _antialiasingEnabled = false;
            _msaaSampleCount = 4; // 默认使用4x MSAA
        }

        // 添加抗锯齿设置方法
        public void SetAntialiasing(bool enable)
        {
            if (_antialiasingEnabled != enable)
            {
                _antialiasingEnabled = enable;

                // 如果已经初始化了设备和资源，需要释放并重新创建它们
                if (_device.Handle != null)
                {
                    // 释放与渲染相关的资源
                    _renderTarget.DisposeIfNotNull();
                    _resolveTexture.DisposeIfNotNull();

                    // 重新创建渲染目标
                    CreateRenderTargets();
                }
            }
        }

        public void SetTransform(MatrixTransform transform)
        {
            _transform = transform;
            _transformSet = true;
        }

        private string GetShaderCode()
        {
            return """
            cbuffer ScreenBuffer : register(b0)
            {
                float2 screenSize;  // width, height
                float3x3 transform; // 变换矩阵
            };

            struct vs_in 
            {
                float2 position : POSITION;
                float4 color    : COLOR;
            };

            struct vs_out
            {
                float4 position : SV_POSITION;
                float4 color    : COLOR;
            };

            vs_out vs_main(vs_in input) 
            {
                vs_out output;
                
                // 应用变换矩阵
                float3 pos = float3(input.position, 1.0f);
                float3 transformedPos = mul(transform, pos);
                
                // Convert from screen coordinates (0 to width/height) to clip space (-1 to 1)
                float2 normalizedPos;
                normalizedPos.x = (transformedPos.x / screenSize.x) * 2.0f - 1.0f;
                normalizedPos.y = 1.0f - (transformedPos.y / screenSize.y) * 2.0f; // 反转Y轴，因为DirectX中Y轴向下
                
                output.position = float4(normalizedPos, 0.0f, 1.0f);
                output.color = input.color;

                return output;
            }

            float4 ps_main(vs_out input) : SV_TARGET
            {
                return float4(input.color.xyz, 1);
            }
            """;
        }

        // 创建渲染目标纹理
        private void CreateRenderTargets()
        {
            // 确保设备已初始化
            if (_device.Handle == null)
                return;

            // 创建多重采样渲染目标
            var renderTargetDesc = new Texture2DDesc()
            {
                Width = (uint)OutputWidth,
                Height = (uint)OutputHeight,
                ArraySize = 1,
                BindFlags = (uint)BindFlag.RenderTarget,
                CPUAccessFlags = 0,
                Format = Format.FormatB8G8R8A8Unorm,
                MipLevels = 1,
                MiscFlags = 0,
                Usage = Usage.Default,
            };

            if (_antialiasingEnabled)
            {
                // 设置多重采样参数
                renderTargetDesc.SampleDesc = new SampleDesc((uint)_msaaSampleCount, 0);

                // 创建用于解析MSAA的非多重采样纹理
                var resolveDesc = new Texture2DDesc()
                {
                    Width = (uint)OutputWidth,
                    Height = (uint)OutputHeight,
                    ArraySize = 1,
                    BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
                    CPUAccessFlags = 0,
                    Format = Format.FormatB8G8R8A8Unorm,
                    MipLevels = 1,
                    MiscFlags = 0,
                    SampleDesc = new SampleDesc(1, 0), // 不使用多重采样
                    Usage = Usage.Default,
                };

                _device.CreateTexture2D(in resolveDesc, ref Unsafe.NullRef<SubresourceData>(), ref _resolveTexture);
            }
            else
            {
                // 不使用多重采样
                renderTargetDesc.SampleDesc = new SampleDesc(1, 0);
                _resolveTexture = default; // 确保不使用解析纹理
            }

            // 创建渲染目标
            _device.CreateTexture2D(in renderTargetDesc, ref Unsafe.NullRef<SubresourceData>(), ref _renderTarget);
        }

        [MemberNotNull(nameof(_api))]
        [MemberNotNull(nameof(_compiler))]
        private void EnsureInitialized()
        {
            if (_api is not null &&
                _compiler is not null &&
                _device.Handle is not null &&
                _deviceContext.Handle is not null)
            {
                return;
            }

            _api ??= D3D11.GetApi(null, false);
            _compiler ??= D3DCompiler.GetApi();

            int createDeviceError = _api.CreateDevice(ref Unsafe.NullRef<IDXGIAdapter>(), D3DDriverType.Hardware, 0, (uint)CreateDeviceFlag.Debug, ref Unsafe.NullRef<D3DFeatureLevel>(), 0, D3D11.SdkVersion, ref _device, null, ref _deviceContext);
            if (createDeviceError != 0)
            {
                throw new InvalidOperationException("Failed to create device");
            }

            // 创建渲染目标纹理
            CreateRenderTargets();

            // 创建输出缓冲
            var outputBufferDesc = new Texture2DDesc()
            {
                Width = (uint)OutputWidth,
                Height = (uint)OutputHeight,
                ArraySize = 1,
                BindFlags = 0,
                CPUAccessFlags = (uint)CpuAccessFlag.Read,
                Format = Format.FormatB8G8R8A8Unorm,
                MipLevels = 1,
                MiscFlags = 0,
                SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Staging,
            };

            _device.CreateTexture2D(in outputBufferDesc, ref Unsafe.NullRef<SubresourceData>(), ref _outputBuffer);

            string shaderCode = GetShaderCode();
            byte[] shaderCodeBytes = Encoding.ASCII.GetBytes(shaderCode);

            fixed (byte* pShaderCode = shaderCodeBytes)
            {
                ComPtr<ID3D10Blob> errorMsgs = null;
                _compiler.Compile(pShaderCode, (nuint)(shaderCodeBytes.Length), "shader", ref Unsafe.NullRef<D3DShaderMacro>(), ref Unsafe.NullRef<ID3DInclude>(), "vs_main", "vs_5_0", 0, 0, ref _vertexShaderCode, ref errorMsgs);
                if (errorMsgs.Handle != null)
                {
                    string error = Encoding.ASCII.GetString((byte*)errorMsgs.GetBufferPointer(), (int)errorMsgs.GetBufferSize());
                    throw new InvalidOperationException(error);
                }

                _compiler.Compile(pShaderCode, (nuint)(shaderCodeBytes.Length), "shader", ref Unsafe.NullRef<D3DShaderMacro>(), ref Unsafe.NullRef<ID3DInclude>(), "ps_main", "ps_5_0", 0, 0, ref _pixelShaderCode, ref errorMsgs);
                if (errorMsgs.Handle != null)
                {
                    string error = Encoding.ASCII.GetString((byte*)errorMsgs.GetBufferPointer(), (int)errorMsgs.GetBufferSize());
                    throw new InvalidOperationException(error);
                }

                _device.CreateVertexShader(_vertexShaderCode.GetBufferPointer(), _vertexShaderCode.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref _vertexShader);
                _device.CreatePixelShader(_pixelShaderCode.GetBufferPointer(), _pixelShaderCode.GetBufferSize(), ref Unsafe.NullRef<ID3D11ClassLinkage>(), ref _pixelShader);
            }

            var sematicNamePosition = "POSITION";
            var sematicNameColor = "COLOR";
            var sematicNamePositionBytes = Encoding.ASCII.GetBytes(sematicNamePosition);
            var sematicNameColorBytes = Encoding.ASCII.GetBytes(sematicNameColor);

            fixed (byte* pSematicNamePosition = sematicNamePositionBytes)
            {
                fixed (byte* pSematicNameColor = sematicNameColorBytes)
                {
                    InputElementDesc[] inputElementDesc =
                    [
                        new InputElementDesc()
                    {
                        SemanticName = (byte*)pSematicNamePosition,
                        SemanticIndex = 0,
                        Format = Format.FormatR32G32Float,
                        InputSlot = 0,
                        InputSlotClass = InputClassification.PerVertexData,
                    },
                    new InputElementDesc()
                    {
                        SemanticName = (byte*)pSematicNameColor,
                        SemanticIndex = 0,
                        Format = Format.FormatR8G8B8A8Unorm,
                        InputSlot = 0,
                        InputSlotClass = InputClassification.PerVertexData,
                        AlignedByteOffset = 2 * sizeof(float),
                    },
                ];

                    _device.CreateInputLayout(in inputElementDesc[0], 2, _vertexShaderCode.GetBufferPointer(), _vertexShaderCode.GetBufferSize(), ref _inputLayout);
                }
            }

            // 创建常量缓冲区 - 现在需要更大空间来存储变换矩阵
            // float2 screenSize + float3x3 transform (需要16字节对齐)
            BufferDesc constBufferDesc = new BufferDesc
            {
                BindFlags = (uint)BindFlag.ConstantBuffer,
                // 常量缓冲区结构：
                // float2 screenSize; (8字节，但会被填充到16字节)
                // float3x3 transform; (9个float，36字节，但会被填充到48字节)
                // 总共 64 字节
                ByteWidth = 64,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
                Usage = Usage.Dynamic
            };

            _device.CreateBuffer(in constBufferDesc, ref Unsafe.NullRef<SubresourceData>(), ref _constantBuffer);
        }

        public void SetMesh(ReadOnlySpan<MeshVertexAndColor> verticesAndColors, ReadOnlySpan<MeshTriangleIndices> indices)
        {
            EnsureInitialized();

            _vertexBuffer.DisposeIfNotNull();
            _indexBuffer.DisposeIfNotNull();

            var castedVerticesAndColors = MemoryMarshal.Cast<MeshVertexAndColor, float>(verticesAndColors);
            var castedIndices = MemoryMarshal.Cast<MeshTriangleIndices, uint>(indices);

            fixed (float* vertexData = castedVerticesAndColors)
            {
                BufferDesc bufferDesc = new BufferDesc
                {
                    BindFlags = (uint)BindFlag.VertexBuffer,
                    ByteWidth = (uint)(sizeof(float) * castedVerticesAndColors.Length),
                    CPUAccessFlags = 0,
                    MiscFlags = 0,
                    Usage = Usage.Default,
                };

                SubresourceData data = new SubresourceData
                {
                    PSysMem = vertexData,
                };

                _device.CreateBuffer(in bufferDesc, in data, ref _vertexBuffer);
            }

            fixed (uint* indexData = castedIndices)
            {
                BufferDesc bufferDesc = new BufferDesc
                {
                    BindFlags = (uint)BindFlag.IndexBuffer,
                    ByteWidth = (uint)(sizeof(uint) * castedIndices.Length),
                    CPUAccessFlags = 0,
                    MiscFlags = 0,
                    Usage = Usage.Default,
                };

                SubresourceData data = new SubresourceData
                {
                    PSysMem = indexData,
                };

                _device.CreateBuffer(in bufferDesc, in data, ref _indexBuffer);
            }
        }

        public void Render(Span<byte> bgraOutput)
        {
            EnsureInitialized();

            if (_vertexBuffer.Handle == null ||
                _indexBuffer.Handle == null)
            {
                throw new InvalidOperationException("No mesh specified!");
            }

            if (bgraOutput.Length != (OutputWidth * OutputHeight * 4))
            {
                throw new ArgumentException("Size not match", nameof(bgraOutput));
            }

            // 更新常量缓冲区中的屏幕尺寸和变换矩阵
            MappedSubresource mappedConstBuffer = default;
            _deviceContext.Map(_constantBuffer, 0, Map.WriteDiscard, 0, ref mappedConstBuffer);

            float* bufferData = (float*)mappedConstBuffer.PData;

            // 设置屏幕尺寸
            bufferData[0] = OutputWidth;
            bufferData[1] = OutputHeight;

            // 从第16字节开始设置变换矩阵（按行主序）
            float* matrixData = bufferData + 4; // 跳过前16字节（screenSize后面有填充）

            // 在着色器中使用的3x3矩阵
            // [ M11 M12 0 ]
            // [ M21 M22 0 ]
            // [ OffsetX OffsetY 1 ]
            matrixData[0] = _transform.M11;
            matrixData[1] = _transform.M12;
            matrixData[2] = 0;
            matrixData[3] = 0; // 填充

            matrixData[4] = _transform.M21;
            matrixData[5] = _transform.M22;
            matrixData[6] = 0;
            matrixData[7] = 0; // 填充

            matrixData[8] = _transform.OffsetX;
            matrixData[9] = _transform.OffsetY;
            matrixData[10] = 1;
            matrixData[11] = 0; // 填充

            _deviceContext.Unmap(_constantBuffer, 0);

            // 创建一个描述无剔除的光栅化状态，启用抗锯齿
            RasterizerDesc rastDesc = new RasterizerDesc
            {
                FillMode = FillMode.Solid,
                CullMode = CullMode.None,   // 不剔除任何三角形
                FrontCounterClockwise = false,
                DepthBias = 0,
                DepthBiasClamp = 0.0f,
                SlopeScaledDepthBias = 0.0f,
                DepthClipEnable = true,
                ScissorEnable = false,
                MultisampleEnable = _antialiasingEnabled,  // 根据抗锯齿设置启用多重采样
                AntialiasedLineEnable = _antialiasingEnabled  // 根据抗锯齿设置启用线条抗锯齿
            };

            ComPtr<ID3D11RasterizerState> rasterizerState = default;
            _device.CreateRasterizerState(in rastDesc, ref rasterizerState);

            ComPtr<ID3D11RenderTargetView> renderTargetView = default;
            _device.CreateRenderTargetView<ID3D11Texture2D, ID3D11RenderTargetView>(_renderTarget, in Unsafe.NullRef<RenderTargetViewDesc>(), ref renderTargetView);

            var viewport = new Viewport(0, 0, OutputWidth, OutputHeight, 0, 1);

            _deviceContext.RSSetViewports(1, in viewport);
            _deviceContext.OMSetRenderTargets(1, ref renderTargetView, ref Unsafe.NullRef<ID3D11DepthStencilView>());
            _deviceContext.RSSetState(rasterizerState);

            _deviceContext.VSSetShader(_vertexShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);
            _deviceContext.PSSetShader(_pixelShader, ref Unsafe.NullRef<ComPtr<ID3D11ClassInstance>>(), 0);

            // 设置常量缓冲区到顶点着色器
            _deviceContext.VSSetConstantBuffers(0, 1, ref _constantBuffer);

            _deviceContext.IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
            _deviceContext.IASetInputLayout(_inputLayout);

            uint vertexStride = sizeof(float) * 2 + 4;
            uint vertexOffset = 0;

            _deviceContext.IASetVertexBuffers(0, 1, ref _vertexBuffer, in vertexStride, in vertexOffset);
            _deviceContext.IASetIndexBuffer(_indexBuffer, Format.FormatR32Uint, 0);

            // 清除渲染目标视图
            //float[] clearColor = new float[4] { 0.0f, 0.0f, 0.0f, 0.0f };
            //_deviceContext.ClearRenderTargetView(renderTargetView, in clearColor[0]);

            // 绘制
            _deviceContext.DrawIndexed((uint)300, 0, 0);

            // 如果启用抗锯齿，需要先解析MSAA纹理到非MSAA纹理
            if (_antialiasingEnabled && _resolveTexture.Handle != null)
            {
                // 解析多重采样纹理
                _deviceContext.ResolveSubresource(_resolveTexture, 0, _renderTarget, 0, Format.FormatB8G8R8A8Unorm);

                // 将解析后的纹理复制到输出
                _deviceContext.CopyResource(_outputBuffer, _resolveTexture);
            }
            else
            {
                // 不需要解析，直接复制到输出
                _deviceContext.CopyResource(_outputBuffer, _renderTarget);
            }

            // 从输出缓冲映射到CPU内存
            MappedSubresource mappedSubResource = default;
            _deviceContext.Map(_outputBuffer, 0, Map.Read, 0, ref mappedSubResource);

            fixed (byte* outputPtr = bgraOutput)
            {
                for (int y = 0; y < OutputHeight; y++)
                {
                    var lineBytes = OutputWidth * 4;
                    NativeMemory.Copy((byte*)((nint)mappedSubResource.PData + mappedSubResource.RowPitch * y), outputPtr + lineBytes * y, (nuint)lineBytes);
                }
            }

            _deviceContext.Unmap(_outputBuffer, 0);

            rasterizerState.Dispose();
            renderTargetView.Dispose();
        }

        ~MeshRenderer()
        {
            _instanceCount--;
            Dispose();
        }

        public void Dispose()
        {
            _device.DisposeIfNotNull();
            _deviceContext.DisposeIfNotNull();
            _vertexBuffer.DisposeIfNotNull();
            _indexBuffer.DisposeIfNotNull();
            _renderTarget.DisposeIfNotNull();
            _outputBuffer.DisposeIfNotNull();
            _resolveTexture.DisposeIfNotNull(); // 释放MSAA解析纹理
            _vertexShader.DisposeIfNotNull();
            _pixelShader.DisposeIfNotNull();
            _inputLayout.DisposeIfNotNull();
            _constantBuffer.DisposeIfNotNull();

            if (_instanceCount == 0)
            {
                _api?.Dispose();
                _compiler?.Dispose();

                _api = null;
                _compiler = null;
            }
        }
    }

}
