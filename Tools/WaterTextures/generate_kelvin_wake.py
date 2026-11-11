"""Generates Assets/WaterSystem/Textures/Wake/KelvinWakeAtlas.exr, the precomputed ship wave pattern.

    python Tools/WaterTextures/generate_kelvin_wake.py

A hull moving at constant speed leaves a wave pattern that is stationary in its own frame, so nothing has
to be simulated at run time: OceanWakeSystem only bends this fixed pattern along each ship's trail.

The pattern is the far-field wave part of the linear solution for a Gaussian pressure patch moving over
deep water (Havelock). With lengths in hull lengths L, Froude number Fr = U / sqrt(g L), k0 = 1 / Fr^2
and t = tan(theta) for the wave direction theta:

    h(x, y) = -Int  A(t) sin(k0 sqrt(1 + t^2) (x + y t)) dt / (1 + t^2)
    A(t)    = (1 + t^2)^2 exp(-k0^2 (1 + t^2) (a^2 + b^2 t^2) / 2)

x is the distance behind the patch and y the lateral offset; a and b are the patch sigmas along and across
the hull. Low Froude numbers give the transverse-wave dominated wedge, high ones the narrow feathered
wake of a planing hull.

Atlas layout (must match OceanWakeSystem.Kelvin*): SLICES rows of NX x NY texels, slice 0 (lowest Froude
number) at the BOTTOM of the texture. Inside a slice u covers x in [0, X_EXTENT] with texel centres at
(i + 0.5) / NX, and v covers |y| in [0, Y_EXTENT] with the first texel centre exactly on the centre line.
R = height normalised to a peak of 1, G = dh/dx, B = dh/d|y| (both per hull length).
Slices share the same phase when sampled at the same k0-scaled position, which is what lets the run-time
blend two neighbouring slices without ghosting.
"""
import os
import time

import numpy as np

from watertex import write_exr_rgb_half

OUTPUT = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', 'WaterSystem', 'Textures', 'Wake',
                      'KelvinWakeAtlas.exr')

FROUDE = [0.28, 0.345, 0.424, 0.522, 0.643, 0.791, 0.974, 1.2]   # geometric progression
X_EXTENT, Y_EXTENT = 16.0, 5.8                                    # hull lengths
NX, NY = 512, 192
SIGMA_LENGTH, SIGMA_BEAM = 0.22, 0.07                             # pressure patch size in hull lengths
RAMP = 0.6                                                        # fade-in behind the patch, hull lengths
SAMPLES = 2048


def kelvin(x, y, froude):
    k0 = 1.0 / (froude * froude)
    limit = 1.0
    while 0.5 * k0 * k0 * (1 + limit * limit) * (SIGMA_LENGTH ** 2 + SIGMA_BEAM ** 2 * limit * limit) < 16 and limit < 14:
        limit += 0.25
    t = np.linspace(-limit, limit, SAMPLES)
    s = np.sqrt(1 + t * t)
    amplitude = (s ** 4 * np.exp(-0.5 * k0 * k0 * (1 + t * t) * (SIGMA_LENGTH ** 2 + SIGMA_BEAM ** 2 * t * t))
                 / (1 + t * t) * (t[1] - t[0]))
    height = np.zeros(x.shape)
    slope_x = np.zeros(x.shape)
    slope_y = np.zeros(x.shape)
    for i in range(SAMPLES):
        if amplitude[i] < 1e-10:
            continue
        k = k0 * s[i]
        phase = k * (x + y * t[i])
        cosine = np.cos(phase)
        height -= amplitude[i] * np.sin(phase)
        slope_x -= amplitude[i] * k * cosine
        slope_y -= amplitude[i] * k * t[i] * cosine
    # The omitted local (non-wave) part would smooth the start of the pattern; a short ramp does it instead.
    r = np.clip(x / RAMP, 0, 1)
    window = r * r * (3 - 2 * r)
    return height * window, slope_x * window + height * (6 * r * (1 - r) / RAMP), slope_y * window


def main():
    start = time.time()
    x, y = np.meshgrid((np.arange(NX) + 0.5) / NX * X_EXTENT, np.arange(NY) / (NY - 1) * Y_EXTENT)
    atlas = np.zeros((NY * len(FROUDE), NX, 3), np.float32)   # row 0 = bottom of the texture
    for index, froude in enumerate(FROUDE):
        height, slope_x, slope_y = kelvin(x, y, froude)
        peak = np.abs(height).max()
        atlas[index * NY:(index + 1) * NY] = np.stack([height, slope_x, slope_y], axis=-1) / peak
        print('Fr %.3f  peak %.4f  (%.1fs)' % (froude, peak, time.time() - start))
    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    write_exr_rgb_half(OUTPUT, atlas[::-1])                   # EXR stores the top scanline first
    print('wrote %s (%dx%d)' % (os.path.normpath(OUTPUT), NX, NY * len(FROUDE)))


if __name__ == '__main__':
    main()
