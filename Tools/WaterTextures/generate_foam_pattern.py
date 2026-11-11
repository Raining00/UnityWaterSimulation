"""Generates Assets/WaterSystem/Textures/Foam/OceanFoamPattern.png, the packed foam pattern of the ocean shader.

    python Tools/WaterTextures/generate_foam_pattern.py [size=1024]

All four channels tile seamlessly and hold linear data (import with sRGB off, no compression):

  R  lace erosion field. Uniformly distributed, brightest on the walls of a three-level cell hierarchy.
     The shader draws foam where R > 1 - coverage, so decaying coverage thins solid foam into a web of
     filaments and finally into specks, the way real sea foam dissolves.
  G  bubble field. Uniformly distributed dots of two sizes; thresholded near 1 for sparse bubble specks.
  B  relief of dense foam (soft billows around 0.5), used to shade thick foam.
  A  macro clumps (uniformly distributed, ridged). Sampled at a larger scale to vary coverage so that the
     dissolve does not happen everywhere at once.
"""
import os
import sys
import time

import numpy as np

from watertex import blur, equalize, spectral_noise, voronoi, write_png

OUTPUT = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', 'WaterSystem', 'Textures', 'Foam',
                      'OceanFoamPattern.png')


def warp(size, seed, strength):
    """Smooth domain warp in cell units; bends the straight Voronoi walls into organic filaments."""
    return (spectral_noise(size, 1.6, seed, 2, 24) * strength,
            spectral_noise(size, 1.6, seed + 1, 2, 24) * strength)


def lace(size):
    # Cell counts per tile. With the default 8 m tile these are roughly 0.9 m, 0.36 m and 0.15 m cells.
    large, _, _ = voronoi(size, 9, 11, warp(size, 80, 0.33))
    medium, _, _ = voronoi(size, 22, 23, warp(size, 164, 0.28))
    small, _, _ = voronoi(size, 55, 37, warp(size, 262, 0.20))

    def wall(edge):
        return 1 - np.clip(edge / 0.42, 0, 1)

    # Finer walls are weighted lower, so they only join the foam at higher coverage.
    field = np.maximum(np.maximum(wall(large) ** 1.6, 0.80 * wall(medium) ** 1.3), 0.58 * wall(small))
    field += spectral_noise(size, 1.3, 91, 2, 40) * 0.10     # varies wall thickness
    field += spectral_noise(size, 0.6, 5, 40, 400) * 0.03    # frays the edges
    return equalize(field)


def bubbles(size):
    _, coarse, coarse_value = voronoi(size, 120, 51, jitter=1.0)
    _, fine, fine_value = voronoi(size, 300, 52, jitter=1.0)
    large = np.clip(1 - coarse / (0.22 + 0.30 * coarse_value), 0, 1) * (0.35 + 0.65 * coarse_value)
    small = np.clip(1 - fine / (0.25 + 0.30 * fine_value), 0, 1) * (0.20 + 0.50 * fine_value)
    return equalize(np.maximum(large, small) + spectral_noise(size, 0.5, 6, 30, 500) * 0.01)


def relief(size):
    _, large, _ = voronoi(size, 14, 77, warp(size, 300, 0.30))
    _, small, _ = voronoi(size, 36, 78, warp(size, 310, 0.25))
    billow = 0.62 * np.clip(1 - large / 0.75, 0, 1) ** 0.7 + 0.38 * np.clip(1 - small / 0.75, 0, 1) ** 0.7
    billow = blur(billow + spectral_noise(size, 1.4, 7, 3, 300) * 0.05, 1.2 * size / 1024)
    return np.clip(0.5 + (billow - billow.mean()) / (4.0 * billow.std()), 0, 1)


def clumps(size):
    ridged = 1 - np.abs(spectral_noise(size, 1.7, 8, 1, 48)) * 0.8 + 0.35 * spectral_noise(size, 1.7, 9, 1, 48)
    return equalize(blur(ridged, 2.0 * size / 1024))


def main():
    size = int(sys.argv[1]) if len(sys.argv) > 1 else 1024
    start = time.time()
    rgba = np.stack([lace(size), bubbles(size), relief(size), clumps(size)], axis=-1)
    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    write_png(OUTPUT, rgba)
    print('wrote %s (%dx%d) in %.1fs' % (os.path.normpath(OUTPUT), size, size, time.time() - start))


if __name__ == '__main__':
    main()
