# FFT 深海海洋系统

Unity 6000.5 / URP 17.5。独立实现深水 PM 频谱、GPU 二维 IFFT、四叉树实例网格和海水表面渲染。参考本工程所拥有的 KWS2 算法流程，未引入其运行时脚本依赖。

## 使用

打开 `Assets/WaterSystem/Demo/DeepOceanDemo.unity`，进入 Play。场景包含已绑定的 FFT Ocean、太阳、天空、固定距离标记以及用于观察折射/焦散的水下平台。Scene 视图可自由移动观察，选中 FFT Ocean 可查看几何和模拟统计。

创建海洋只需给空物体添加 `Water System > Ocean`（类名仍为 OceanRenderer），或执行 `GameObject > Water System > Ocean`。OceanSettings 和 FFTCompute 自动加入同一个物体并建立依赖，无须拖拽脚本、ComputeShader 或 Material。创建菜单还会为当前 URP 资产启用 Depth Texture / Opaque Texture；本工程已启用。

也可拖入 `Demo/FFT Ocean.prefab`。用于观察海面的相机应使用 URP，允许生成深度和不透明颜色纹理；若手动覆盖相机设置，不要将这两个选项关闭。

默认开启编辑器模拟。可在 OceanRenderer 上关闭 Preview Simulation，仅在 Play 中计算。`OceanSettings.timeScale=0` 暂停；更改风、谱参数会重建初始谱，更改输出幅度会立即反映到后续输出。重初始化按钮会清空泡沫历史。

渲染参数集中在主组件的 Rendering 中，也可以直接用 C# 调整；材质由组件内部创建和释放，不显示 Material 资产槽，不需要创建 .mat：

```csharp
using WaterSystem.Ocean;
using UnityEngine;

var ocean = new GameObject("Ocean").AddComponent<OceanRenderer>();
ocean.Waves.windSpeed = 10;
ocean.Waves.choppiness = 1.25f;
ocean.Rendering.Roughness = 0.2f;
ocean.Rendering.DeepColor = new Color(0.006f, 0.09f, 0.12f);
ocean.Rendering.Absorption = new Vector3(0.22f, 0.065f, 0.035f);
ocean.Rendering.FoamStrength = 1.5f;
ocean.InfiniteHorizon = true;
```

调整 Rendering 会在下次相机渲染时应用，不重建材质、初始频谱或泡沫历史。旧场景的 SurfaceMaterial 通过隐藏迁移字段导入一次外观参数后清空；原有材质文件保留以兼容旧资产，不再参与新海洋的创建。

## 文件与职责

- `Runtime/Geometry`：四叉树、2:1 邻接平衡、16 种接缝网格。
- `Runtime/Rendering` 与 `OceanRenderer.cs`：相机裁剪、实例矩阵提交、每帧一次模拟与每相机资源绑定。
- `Runtime/Rendering/OceanRenderSettings.cs`：C# 外观参数及内部材质更新。
- `Runtime/Geometry/OceanHorizonMesh.cs`：覆盖屏幕的远海解析平面和省略远平面的水面裁剪。
- `Resources/WaterSystem/OceanDefaults.asset`：默认 Shader、OceanFFT.compute、泡沫/焦散纹理及实例化 Shader 变体引用，保证运行时及构建中能够加载。
- `Runtime/Simulation/Settings`：风速/方向、PM 常数、四层周期、时间、波高、水平位移和泡沫设置。
- `Runtime/Simulation/Parameters`：实际空间/频率参数、初始化失效判断。
- `Runtime/Simulation/Resources`：复数频谱、中间 FFT、输出纹理和泡沫历史。
- `Runtime/Simulation/FFT`：C# 调度、蝶形表、Shader 属性约定。
- `Shaders/OceanFFT.compute`：频谱初始化、演化、IFFT、位移、法线/Jacobian、泡沫生成与衰减。
- `Shaders/OceanSurface.shader`：URP 实例化水面 Shader。
- `Editor/OceanShowcaseBuilder.cs`：配置菜单与演示生成。
- `Editor/OceanBuildProcessor.cs`：构建前自动将 Instancing Stripping 设为 Keep All，防止运行时创建的实例化材质被 Unity 的场景扫描误判为未使用；无需手动配置。此设置会保留工程内其他 Shader 的实例化变体，可能增加构建体积，不增加运行时绘制提交。

## 模拟流程

每层使用独立 N×N 频谱和物理周期 L。默认 N=128、L=5/20/100/600m，可独立修改，与网格分辨率无绑定。示例开启频带分隔，减少不同层重复累计相同波段能量。

1. 初始谱：采用 PM 频谱、方向扩散、深水色散 `ω²=g|k|`。用可复现的高斯随机数生成复数 H0，镜像坐标重新计算同一随机数，存储 `(h0(k), conj(h0(-k)))`。
2. 时间演化：计算正负相位，形成满足共轭对称的高度谱；由 `-i*k/|k|` 生成水平位移谱，Nyquist 轴的对应奇导数置零。
3. IFFT：Radix-2 DIT，CPU 预制蝶形表，每轴 log2N 级；X/Y/Z 三个复数分量顺序复用 Ping/Pong，转换后保存各自空间场。
4. 输出：一次性处理中心化符号 `(-1)^(x+y)`、`1/N²`、每层高度倍率与 choppiness。H0 中的 N² 与归一化匹配，使物理幅度不因 N 增大被缩小。
5. 法线：周期边界中心差分，计算变形表面的切向量、法线和水平映射 Jacobian。
6. 泡沫：压缩到 Jacobian 阈值以下时持续生成，历史值指数衰减；两张 R16 数组纹理交替使用。泡沫来自实际波形压缩，不是覆盖全海面的循环白色贴图。

稳定帧 N=128 时为 46 次 Compute Dispatch，首帧增加一次初始谱和两次泡沫清零。多相机复用同一次模拟结果。该调度以清晰可扩展为先，尚未将多级蝶形融合为共享内存 Kernel。

纹理约 `76*N*N*C + 16*N*log2N` 字节，128/4 层约 4.76 MiB，512/4 层约 76.07 MiB，不含驱动对齐与几何。输出位移 RGB 为世界米；法线 RGB 为有符号单位向量，A 为 Jacobian；泡沫 R 为覆盖度。

## 海水渲染

共享网格顶点经实例矩阵转为世界位置，再以未位移世界 XZ 采样各层位移，因此四叉树接缝两侧获得相同波形。片元阶段合成各层法线斜率，并根据屏幕采样足迹抑制远处高频闪烁。

水面在不透明物体之后绘制，显式合成颜色并写入深度，以解决波面重叠：

- 水—空气界面的 Schlick Fresnel，F0≈0.02037。
- URP 环境反射（无有效环境时使用可调天空近似），以及 GGX 太阳高光。
- 不透明颜色与深度驱动的折射，拒绝采到前景或水面上方的扭曲坐标。
- RGB Beer–Lambert 吸收、随厚度变化的水色和波峰背光散射。
- 主光阴影与 URP 雾。
- Jacobian 泡沫历史叠加 KWS2 泡沫细节纹理。
- 对水下可见不透明表面，在水面折射合成阶段加入双重滚动焦散图案，随深度衰减。

水下平台只是渲染参照物，不会改变 FFT 深水波浪。焦散属于纹理投影近似，不是追踪当前波浪折射光线的物理求解。

## 常用调整

| 目标 | 参数 |
|---|---|
| 更强的海况 | windSpeed；影响频谱能量和主波长 |
| 放大现有波形 | heightScale / wavesDomainHeightScales |
| 更尖锐的波峰 | choppiness；过大可能折叠并增加白沫 |
| 更多或更持久的白沫 | 增大 foamThreshold / foamBuildRate，减小 foamDecay |
| 更通透 | Rendering.Absorption，减小对应 RGB 吸收系数 |
| 更深或更绿的水色 | Rendering.DeepColor / ShallowColor |
| 较宽的高光 | 增大 Rendering.Roughness |
| 反射、折射、背光强度 | Rendering.ReflectionStrength / RefractionStrength / SubsurfaceStrength |
| 表面法线、白沫外观 | Rendering.NormalStrength / FoamStrength / FoamColor / FoamScale |
| 焦散外观 | Rendering.CausticStrength / CausticScale / CausticDepth |
| 降低成本 | 减少 FFT 层数/分辨率，或降低几何 MaxDepth / PatchResolution |

位移幅度增大后同步提高 maximumDisplacement，避免 CPU 剔除仍可能移入屏幕的波峰。

## 无限远海

默认开启 InfiniteHorizon。RootSize 现在控制近中距离的 FFT 四叉树区域；外侧用一个 3 顶点全屏三角形逐像素计算视线与海平面的交点，默认最大着色距离 HorizonDistance=100000m，并按相机位置和视锥自动扩大。远海与所有接缝网格共享同一个内部海洋材质，每台相机增加一次绘制提交。

根区域的最后 20% 半径（距中心 0.4×RootSize 至 0.5×RootSize）平滑衰减 FFT 位移、法线和白沫，在外边界归零，与平坦远海相接。远海向内保留 0.1% 的深度测试重叠，用于闭合独立光栅化产生的单像素边界裂缝。普通 Patch 显式写入 UV2=0，解析远海写入 UV2.y=1，不依赖显卡对缺失顶点通道的默认值。

CPU 对无限水面保留侧面和近裁剪，省略远裁剪；详细网格在 Shader 中保留真实投影 XY/W，并将远处水面深度限制在远裁剪面以内。解析远海不再生成跨越相机和 Far Clip 的公里级三角形，从根源上避免三角形插值精度造成的黑色条带。片元写入由真实世界交点计算的深度，不会错误遮挡近处物体。此处理仅影响水面，不修改 Camera.farClipPlane。50m 远裁剪、相机移出固定根区域和正交相机均有渲染验证。

关闭 InfiniteHorizon 可恢复有限四叉树平面。延展是视觉上的平坦远海，不包含地球曲率；非常高空视角可继续提高 HorizonDistance。扩大 RootSize 可使有实际 FFT 位移的区域更远。

## 素材来源

复制自当前已导入的 `WaterSample/Assets/KriptoFX/WaterSystem2/WaterResources/Resources/Textures`：

- `FluidsFoamTex.png`：泡沫细节，采用 R 通道，导入上限 1024。
- `Caustic/Lod0/Caustic_000.png`：焦散图案，导入上限 512。

两者保留原图片内容，生成本工程自己的 GUID 和导入设置。它们是现有 KWS2 素材，不作为新创作或公共授权素材声明。

## 当前边界

这是完整的深水 FFT 到海面成像流程，范围不包含浅水方程、岸线耦合、物体激起的波浪、浮力、泡沫粒子、SSR/平面反射、体积光或水下全屏后处理。背面有基础水色衰减，但不等同于完整水下相机效果。折射只能看到不透明颜色副本中的内容；透明物体不能通过该副本折射。没有水面投影阴影、运动矢量、XR 双眼联合裁剪或 LOD 几何形变过渡。
