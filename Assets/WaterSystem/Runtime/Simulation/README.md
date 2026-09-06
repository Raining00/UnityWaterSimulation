# C# 海洋频谱模拟：参数、资源与接入约定

本文件说明 `OceanSettings`、`OceanTextures`、`FFTCompute` 的数据和调度契约。完整 Kernel 已在 `Assets/WaterSystem/Shaders/OceanFFT.compute` 实现，配套 `WaterSystem/Ocean` Shader 用于海水渲染。打开 `Demo/DeepOceanDemo.unity` 可直接运行，完整使用说明见 `Assets/WaterSystem/README.md`。添加 OceanRenderer 会自动创建依赖组件；FFT 从 OceanDefaults 资源自动加载唯一默认 ComputeShader，不再要求手工指定。

## 1. 代码组织与已有数据

```text
Runtime/Simulation/
  OceanSimulationProvider.cs             现有的渲染—模拟生命周期接口
  Settings/OceanSettings.cs              Inspector 可编辑输入，兼容原有字段名
  Parameters/OceanSpectrumParameters.cs  统一推导出的实际参数和初始化指纹
  Resources/OceanTextures.cs             GPU 资源所有权、分配、重分配和释放
  FFT/FFTCompute.cs                      生命周期、时间、Kernel 和 IFFT 调度
  FFT/OceanButterfly.cs                  CPU 生成基 2 蝶形查找表
  FFT/OceanFFTShaderIDs.cs                C# / Shader 属性名称唯一来源
  README.md
Editor/
  FFTComputeEditor.cs                    状态、实际参数、显存估算与重初始化按钮
```

已有三个脚本移动时连同 `.meta` 一起移动，保留 GUID、类名和命名空间；`OceanSettings` 仍是 MonoBehaviour，已有风、时间、分辨率、层数和周期字段保留名称与类型。没有改成 ScriptableObject，以免已有场景组件引用失效。

`OceanWorldSpaceBounds` 原来没有使用且属于几何职责，移出设置类；世界包围盒继续由 OceanRenderer 与四叉树维护，模拟只报告 `MaximumDisplacement`。删除了运行时代码对 `PlasticGui` 的编辑器程序集依赖。

`FFTDisplayment` 拼写保留为带 Obsolete 标记的只读兼容别名；新代码使用 `FFTDisplacement`。`FFTRow`、`FFTColumn` 都指向同一个蝶形表：行、列的蝶形系数相同，变换方向用 `_FFTAxis` 区分。资源属性不能任意外部赋值，避免替换后泄漏。

## 2. 输入参数与单位

### 风与频谱

| 参数 | 含义 |
|---|---|
| windSpeed | 手动风速，m/s；零风速必须产生零频谱 |
| windRotation | 度；0 指向世界 +X，90 指向世界 +Z |
| windTurbulence | 0～1，方向扩散强度，具体方向分布由频谱 Kernel 实现 |
| windZone / 两个 Multiplier | 方向型 WindZone 的 forward 投影到 XZ；windMain × multiplier 被解释为 m/s，windTurbulence × multiplier 限制到 0～1 |
| gravity | g，默认 9.81 m/s²；这里只约定深水色散 |
| randomSeed | 初始随机种子；Compute 应结合级联索引和格点坐标派生随机数，避免各层重复噪声 |
| spectrumAmplitude | 频谱能量倍率，作用于功率谱而非最终位移幅度 |
| spectrumAlpha / spectrumBeta | PM 谱常量，默认 0.0081 / 1.291，与所参考的 KWS2 公式对应 |
| peakFrequencyFactor | 峰值角频率系数，默认 0.87 |
| shortWaveDamping | 小波衰减长度 l，m；建议在功率谱中乘 exp(-k²l²)，0 表示不衰减 |
| cascadeTurbulenceFloor | 每层方向扩散的最小值，默认 0.5、0.25、0、0 |

WindZone 的 windMain 本身是 Unity 风力参数，并非自动具有 m/s 单位；倍率是此实现的显式换算。球形风场、脉冲不适用于单一均匀海面风向，使用手动参数；竖直风向投影接近零时使用手动方向。参见 [Unity WindZone](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/WindZone.html)。

### 布局、级联和输出

| 参数 | 含义 |
|---|---|
| fftWaveQuality | N，32～512 的 2 次幂；与几何 PatchResolution 无关 |
| fftWaveCascades | 活跃层数，1～4；保留四层参数，减少层数不会丢失未使用层的配置 |
| waveAreaScale | 世界周期统一缩放；同时影响空间采样间距和波数 |
| wavesDomainSizes | 从小到大的周期，默认 5、20、100、600 m |
| wavesDomainHeightScales | 每层最终幅度倍率，默认 0.5、0.5、0.6、0.9 |
| separateCascadeBands | 可选频带分隔，默认关闭以保留原始多层叠加方式 |
| heightScale / choppiness | 全局波浪幅度 / 相对于高度的水平位移倍率 |
| normalStrength | 输出法线斜率强度，默认 1 |
| maximumDisplacement | 手动保守上界 `(水平绝对位移, 竖直绝对位移)`，用于裁剪；不是 RMS 波高或统计估计 |
| timeScale / timeOffset | 模拟速率 / 初始或追加时间偏移；timeScale=0 暂停 |
| loopPeriod | 秒；0 不循环，正值要求 Kernel 将角频率量化到 2π/T 的整数倍 |

Validate 会修复 null/短数组、限制层数与分辨率、确保周期正值且严格递增，并处理 NaN/Infinity。有限但极端的物理参数仍应由使用者合理设置。高度、水平位移倍率增大时，应同步增大 maximumDisplacement；当前不尝试从随机谱推断严格最大波高。

## 3. 派生参数

对第 i 层：

```text
L_i       = wavesDomainSizes[i] * waveAreaScale
dx_i      = L_i / N                 空间格点间距，m
deltaK_i  = 2π / L_i                波数格点间距，rad/m
k_axisMax = πN / L_i                单轴 Nyquist 波数
stages    = log2(N)                 每个方向的蝶形级数
IFFTNorm  = 1 / N²                  二维逆变换归一化
omegaPeak = peakFrequencyFactor*g/U 风速非零时，rad/s
windLengthScale = U²/g              风浪特征长度尺度，并非最大波长
omegaLoop = 2π/loopPeriod           循环开启时，rad/s
```

`CascadeGeometry[i] = (L_i, 1/L_i, dx_i, deltaK_i)`。

`CascadeSpectrum[i] = (kMin, kMax, heightScale * layerHeightScale, max(turbulence, layerFloor))`。

不开启频带分隔时，各层最小径向波数为 0，最大值取 `sqrt(2)*πN/L_i`，覆盖方形频谱角落。开启后，最大值为单轴 Nyquist，最小值为下一大周期层的最大值；最后一层最小为 0。建议采用半开区间 `[kMin,kMax)`，避免临界频率重复计能。分隔模式有意截去方形频谱的角落，且可让最小周期层没有有效离散样本，例如 N 很小、相邻 L 太接近时；这是带宽设置问题，不应凭空补入能量。

128×128、4 层时，每轴 7 级，二维三个复数分量共 `3 × 2 × 7 = 42` 次蝶形 Dispatch。稳定帧还包含频谱演化、位移合成、法线计算和泡沫更新，共 46 次 Dispatch；初始谱重建帧再加 1 次，新资源第一次使用再加 2 次泡沫清零。每次 Dispatch 的 Z 覆盖全部级联，并非逐层重复此数量。另有 3 次 CopyTexture 保存空间域分量。

这是一种易于接入、可审查的通用逐级 FFT 调度契约，不声称达到 KWS2 的融合 Kernel 性能。后续可将一个方向的多级蝶形融入共享内存 Kernel，保留 Settings/Parameters/Textures 和 Provider 接口。

## 4. GPU 资源及复数布局

除蝶形表外，所有资源均为 `N×N×CascadeCount` 的线性 Texture2DArray，无深度、MSAA 或 mipmap，开启 UAV。采样与读写格式支持在分配前检查。

| 属性 | 格式 | 内容 / 生存期 |
|---|---|---|
| SpectrumInitial | RGBA32 float | `(Re h0(k), Im h0(k), Re conj(h0(-k)), Im conj(h0(-k)))`；配置不变时复用 |
| SpectrumX/Y/Z | RG32 float | X/Y/Z 位移的复数谱 `(real,imag)`；IFFT 后原地保存未归一化的空间场；下一帧演化覆盖 |
| Ping / Pong | RG32 float | 单个复数分量的蝶形中间结果；三个分量顺序复用，不允许同一 Dispatch 同纹理读写 |
| FFTDisplacement | RGBA16 float | `(Dx,Dy,Dz,0)`，世界单位，已归一化并乘幅度与 choppiness |
| FFTNormal | RGBA16 float | `(signed normal.xyz, Jacobian)`；法线取 [-1,1] 浮点，不做 [0,1] 编码 |
| ButterflyLookup | RGBA32 float Texture2D | `N×log2(N)`；Point + Clamp，CPU 创建后不可读 |
| FoamPrevious / FoamCurrent | R16 float | 浪尖泡沫覆盖度，生成与指数衰减；更新后交换引用，FoamPrevious 为最新输出 |

初始谱、复杂谱和临时纹理使用 Point + Repeat；位移/法线输出使用 Bilinear + Repeat。共享蝶形表只分配一次，不另建 FFTRow 和 FFTColumn。

估算纹理字节数为 `76*N*N*CascadeCount + 16*N*log2(N)`。默认约 4.76 MiB，512/4 层约 76.07 MiB，不含 GPU 对齐、驱动开销、材质或几何内存。资源类在分配失败时清理已创建对象，Allocate 同布局会复用，布局变化会重建，Dispose 可重复调用。

这里选择从位移纹理差分生成法线与 Jacobian，没有额外的频域斜率通道。泡沫已经使用两张历史纹理，以 Jacobian 压缩为来源，并抑制小周期细纹形成过量白沫。

## 5. Compute Shader 契约

FFTCompute 接收 `OceanFFT.compute`，其中实现了以下七个 Kernel。C# 和 HLSL 名称必须保持同步；KWS2 原始 ComputeShader 的名称、资源布局和归一化不同，不能直接替换此资产。

1. `InitializeSpectrum`：写 `_SpectrumInitial`。读取布局、种子、风、PM 谱、级联参数。零风速和 DC 项必须显式置零；所有线程必须检查 `id.x/y<N` 与 `id.z<CascadeCount`。
2. `UpdateSpectrum`：读取 `_SpectrumInitial`，写 `_SpectrumX/Y/Z`。进行色散与时间演化，所有格点完整覆盖写入。
3. `InverseFFT`：读取 `_FFTInput`、`_FFTButterfly`，写 `_FFTResult`。`_FFTAxis=0/1` 表示 X/Y；`_FFTStage` 从 0 开始。每级是一次完整读写，不在这里归一化。
4. `BuildDisplacement`：读取三个已经变为空间场的 `_SpectrumX/Y/Z`，写 `_OceanDisplacement`。统一处理中心化符号、1/N²、每层幅度与水平 choppiness。
5. `BuildNormals`：读取位移数组，用周期边界和每层 dx 差分计算法线、Jacobian，写 `_OceanNormals`。纹理采用双线性采样时，Shader 仍应归一化插值后的法线。
6. `ClearFoam`：将 `_FoamResult` 清零，仅在历史纹理创建时执行。
7. `BuildFoam`：读取 `_FoamPrevious` 和法线纹理中的 Jacobian，写 `_FoamResult`，随后交换两张泡沫纹理。

所有 `SetComputeTextureParam` 都按 Kernel 独立绑定，线程组尺寸由 `GetKernelThreadGroupSizes` 查询后向上整除，而不是写死 8×8。相关 API 参见 [Unity CommandBuffer.SetComputeTextureParam](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetComputeTextureParam.html)。

### 常量打包

| Shader 属性 | 值 |
|---|---|
| _FFTResolution / _FFTCascadeCount / _FFTStageCount | N / 层数 / log2 N，int |
| _FFTStage / _FFTAxis | 当前蝶形级 / 方向，int |
| _FFTNormalization | 1/N² |
| _SpectrumSeed | int；HLSL 可 asuint 保留全部位模式 |
| _OceanSimulationTime / _OceanSimulationDeltaTime | 模拟绝对时间 / 本帧缩放后的时间差，float 秒 |
| _OceanWind | `(dirX, dirZ, speed, turbulence)` |
| _OceanSpectrum | `(energyMultiplier, alpha, beta, omegaPeak)` |
| _OceanDispersion | `(gravity, omegaLoop, shortWaveDampingLength, reserved=0)` |
| _OceanOutput | `(choppiness, normalStrength, reserved=0, reserved=0)` |
| _OceanFoamParameters | `(compressionThreshold, softness, decayRate, buildRate)` |
| _OceanCascadeGeometry[4] | 每层 `(L, 1/L, dx, deltaK)` |
| _OceanCascadeSpectrum[4] | 每层 `(kMin, kMax, heightWeight, turbulence)` |

### 数学和缩放一致性

沿用参考代码的深水 PM 路线，可采用：

```text
omega(k) = sqrt(g * |k|)
domega/dk = g / (2*omega)                  k>0
S(omega) = A * alpha*g²/omega^5 * exp[-beta*(omegaPeak/omega)^4]
P(k) = S(omega) * directionalDistribution * (domega/dk) / |k|
h(k,t) = h0(k)*exp(+i*omega*t) + conj(h0(-k))*exp(-i*omega*t)
Dx(k,t) = -i*kx/|k| * h(k,t)
Dz(k,t) = -i*kz/|k| * h(k,t)
```

此处逆 FFT 采用正指数，蝶形表与直接正指数逆 DFT 对照验证。初始 k 网格采用 `(index-N/2)*deltaK` 时，BuildDisplacement 需要一次 `(-1)^(x+y)` 中心化修正；不要在蝶形和输出两个地方重复修正。

归一化约定必须与 H0 振幅一致：如果希望真实傅里叶级数系数 `a_k` 直接按 `sum a_k*exp(i*k*x)` 表示高度，传入此归一化 IFFT 的离散系数应为 `N²*a_k`。生成 h0 时包含的 Gaussian 方差、正负方向能量分配及 `deltaK²` 也必须统一；不要直接照搬另一 FFT 的谱振幅却再额外除 N²。这里的 C# 显式提供归一化，具体谱采样归一化由后续 Kernel 实现与基准验证确定。

正负频率必须满足实值场的共轭对称，DC 项为零；Nyquist 行/列属于自共轭特殊位置，水平奇导数的对应 Nyquist 分量应作一致处理，避免明显虚部残留。后续 Shader 测试应检查空间场虚部接近零、能量与分辨率变化的关系。

### 蝶形表

`Lookup[output,stage] = (wr,wi,inputA,inputB)`，执行 `out = A + (wr+i*wi)*B`。负号已包含在下半组的 wr/wi 中。每个方向的第 0 级输入索引已经做 bit reversal，后续级索引不反转；不要在 Shader 中再次位逆序。正权重角度为 `2π*j/span`。同一张表重复用于行和列。

## 6. 更新、缓存与生命周期

Initialize 解析同物体的 OceanSettings 和打包的默认 ComputeShader，不依赖 Mesh。每个帧号只记录一次，多个相机绑定相同输出。默认资源缺失时报告明确错误，由 Renderer 捕获并暂停模拟，不创建全局 Shader 参数。

需要重新分配：N、层数、ComputeShader 或显式重初始化改变。

需要重新生成 H0：种子、风速/方向、谱常量、重力、周期、频带、方向扩散或小波衰减改变。初始谱指纹明确排除 timeScale、heightScale、choppiness 等只影响时间或输出的值。改变风向暂时直接重建谱，不做旧谱与新谱的平滑混合。

时间使用帧上下文绝对 Time 的差值，并积分当前 timeScale，避免直接 `absoluteTime*timeScale` 在修改速率时跳相位；也避免上下文中已截断的 DeltaTime 导致低帧率时模拟变慢。loopPeriod>0 时传入取模时间，并要求演化 Kernel 按 omegaLoop 量化频率；仅取模而不量化会跳变。无循环的超长运行最终会遇到 GPU float 时间精度限制，后续可使用高低位时间或相位重基准。

FFTCompute 不执行 CommandBuffer，由 OceanRenderer 在绑定输出前统一执行。`HasOutput` 表示该帧已记录输出命令，不是 GPU 完成 fence。解绑/禁用/销毁释放资源；错误由现有 Renderer 的保护逻辑记录并暂停该 Provider，使用 Reinitialize FFT 重试。

## 7. 在场景中接入

1. 给物体添加 OceanRenderer；RequireComponent 自动补齐 OceanSettings 和 FFTCompute，主组件自动建立引用。
2. 在 OceanSettings 或 `ocean.Waves` 中调整风、频谱、时间和波形；主组件 PreviewSimulation 控制编辑器预览。
3. 默认资源引用 `Shaders/OceanFFT.compute`；重复的 Runtime/Shader/FFTCompute.compute 已删除。
4. 在 `ocean.Rendering` 中调整外观，不需要 Material 资产。内部材质读取 `_OceanDisplacement`、`_OceanNormals`、`_OceanFoam`、`_OceanDomainSizes`、`_FFTResolution` 和 `_FFTCascadeCount`，并判断 `_OceanSimulationReady`。
5. 使用位移前世界 XZ / 每层 L 采样，以世界米叠加各层位移。输出已经乘幅度与 choppiness，材质不要重复乘。
6. 输出按级联存储法线；多层法线不能直接相加后当成最终结果。可以恢复斜率后合成，或在自定义水体 Shader 中从总位移重建正确法线，尤其注意水平位移的 Jacobian。

关闭 PreviewSimulation 可在编辑模式观察静态水面。完整海水 Shader 实现了环境反射、深度折射、吸收/散射、太阳高光、泡沫和焦散，不涉及岸线或浅水耦合；InfiniteHorizon 控制外圈的视觉地平线延展。
