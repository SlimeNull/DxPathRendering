using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
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
        ComPtr<ID3D11Texture2D> _renderTarget;
        ComPtr<ID3D11Texture2D> _outputBuffer;
        ComPtr<ID3D10Blob> _vertexShaderCode;
        ComPtr<ID3D10Blob> _pixelShaderCode;
        ComPtr<ID3D11VertexShader> _vertexShader;
        ComPtr<ID3D11PixelShader> _pixelShader;
        ComPtr<ID3D11InputLayout> _inputLayout;
        ComPtr<ID3D11Buffer> _constantBuffer;

        private int _inputWidth;
        private int _inputHeight;
        private float[]? _verticesAndColors;
        private uint[]? _indices;

        public int OutputWidth { get; }
        public int OutputHeight { get; }

        public MeshRenderer(
            int outputWidth, int outputHeight)
        {
            _instanceCount++;
            OutputWidth = outputWidth;
            OutputHeight = outputHeight;
        }

        private string GetShaderCode()
        {
            return """
                cbuffer ScreenBuffer : register(b0)
                {
                    float2 screenSize;  // width, height
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
                    
                    // Convert from screen coordinates (0 to width/height) to clip space (-1 to 1)
                    float2 normalizedPos;
                    normalizedPos.x = (input.position.x / screenSize.x) * 2.0f - 1.0f;
                    normalizedPos.y = 1.0f - (input.position.y / screenSize.y) * 2.0f; // 反转Y轴，因为DirectX中Y轴向下
                    
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
                SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Default,
            };

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

            _device.CreateTexture2D(in renderTargetDesc, ref Unsafe.NullRef<SubresourceData>(), ref _renderTarget);
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

            // 创建常量缓冲区
            BufferDesc constBufferDesc = new BufferDesc
            {
                BindFlags = (uint)BindFlag.ConstantBuffer,
                ByteWidth = 16, // 对齐到16字节（float2 = 8字节，但常量缓冲区需要16字节对齐）
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
                Usage = Usage.Dynamic
            };

            _device.CreateBuffer(in constBufferDesc, ref Unsafe.NullRef<SubresourceData>(), ref _constantBuffer);
        }

        public void SetMesh(ReadOnlySpan<MeshVertexAndColor> verticesAndColors, ReadOnlySpan<MeshTriangleIndices> indices)
        {
            _verticesAndColors = new float[verticesAndColors.Length * 6];
            _indices = new uint[indices.Length * 3];

            MemoryMarshal.Cast<MeshVertexAndColor, float>(verticesAndColors).CopyTo(_verticesAndColors);
            MemoryMarshal.Cast<MeshTriangleIndices, uint>(indices).CopyTo(_indices);
        }

        public void Render(Span<byte> bgraOutput)
        {
            EnsureInitialized();

            if (_verticesAndColors is null ||
                _indices is null)
            {
                throw new InvalidOperationException("No mesh specified!");
            }

            ComPtr<ID3D11Buffer> vertexBuffer = default;
            ComPtr<ID3D11Buffer> indexBuffer = default;

            fixed (float* vertexData = _verticesAndColors)
            {
                BufferDesc bufferDesc = new BufferDesc
                {
                    BindFlags = (uint)BindFlag.VertexBuffer,
                    ByteWidth = (uint)(sizeof(float) * _verticesAndColors.Length),
                    CPUAccessFlags = 0,
                    MiscFlags = 0,
                    Usage = Usage.Default,
                };

                SubresourceData data = new SubresourceData
                {
                    PSysMem = vertexData,
                };

                _device.CreateBuffer(in bufferDesc, in data, ref vertexBuffer);
            }

            fixed (uint* indexData = _indices)
            {
                BufferDesc bufferDesc = new BufferDesc
                {
                    BindFlags = (uint)BindFlag.IndexBuffer,
                    ByteWidth = (uint)(sizeof(uint) * _indices.Length),
                    CPUAccessFlags = 0,
                    MiscFlags = 0,
                    Usage = Usage.Default,
                };

                SubresourceData data = new SubresourceData
                {
                    PSysMem = indexData,
                };

                _device.CreateBuffer(in bufferDesc, in data, ref indexBuffer);
            }

            if (bgraOutput.Length != (OutputWidth * OutputHeight * 4))
            {
                throw new ArgumentException("Size not match", nameof(bgraOutput));
            }

            // 更新常量缓冲区中的屏幕尺寸
            MappedSubresource mappedConstBuffer = default;
            _deviceContext.Map(_constantBuffer, 0, Map.WriteDiscard, 0, ref mappedConstBuffer);

            float* screenSizeData = (float*)mappedConstBuffer.PData;
            screenSizeData[0] = OutputWidth;
            screenSizeData[1] = OutputHeight;

            _deviceContext.Unmap(_constantBuffer, 0);

            // 创建一个描述无剔除的光栅化状态
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
                MultisampleEnable = false,
                AntialiasedLineEnable = false
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

            _deviceContext.IASetVertexBuffers(0, 1, ref vertexBuffer, in vertexStride, in vertexOffset);
            _deviceContext.IASetIndexBuffer(indexBuffer, Format.FormatR32Uint, 0);

            _deviceContext.DrawIndexed((uint)_indices.Length, 0, 0);
            _deviceContext.CopyResource(_outputBuffer, _renderTarget);

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

            vertexBuffer.Dispose();
            indexBuffer.Dispose();
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
            _renderTarget.DisposeIfNotNull();
            _outputBuffer.DisposeIfNotNull();
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
