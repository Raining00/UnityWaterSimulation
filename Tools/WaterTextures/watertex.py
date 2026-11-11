"""Shared helpers for the water texture generators: tileable noise, Voronoi fields and minimal PNG/EXR writers.

Only numpy is required, so the generators run on a bare Python install.
"""
import struct
import zlib

import numpy as np


# --------------------------------------------------------------------------------------- writers

def write_png(path, array):
    """Write an 8-bit PNG. array: HxW or HxWx{1,2,3,4}, float in [0, 1] or uint8."""
    a = np.asarray(array)
    if a.dtype != np.uint8:
        a = np.clip(a * 255.0 + 0.5, 0, 255).astype(np.uint8)
    if a.ndim == 2:
        a = a[:, :, None]
    height, width, channels = a.shape
    color_type = {1: 0, 2: 4, 3: 2, 4: 6}[channels]
    rows = a.reshape(height, width * channels)
    raw = np.concatenate([np.zeros((height, 1), np.uint8), rows], axis=1).tobytes()  # filter type 0 per row

    def chunk(tag, data):
        body = tag + data
        return struct.pack('>I', len(data)) + body + struct.pack('>I', zlib.crc32(body) & 0xffffffff)

    with open(path, 'wb') as stream:
        stream.write(b'\x89PNG\r\n\x1a\n')
        stream.write(chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, color_type, 0, 0, 0)))
        stream.write(chunk(b'IDAT', zlib.compress(raw, 9)))
        stream.write(chunk(b'IEND', b''))


def write_exr_rgb_half(path, rgb):
    """Write an uncompressed half-float RGB OpenEXR. rgb: HxWx3 float, row 0 is the TOP scanline."""
    rgb = np.asarray(rgb, dtype=np.float16)
    height, width, _ = rgb.shape

    def attribute(name, kind, value):
        return name.encode() + b'\0' + kind.encode() + b'\0' + struct.pack('<i', len(value)) + value

    channels = b''.join(name + b'\0' + struct.pack('<iBxxxii', 1, 0, 1, 1) for name in (b'B', b'G', b'R')) + b'\0'
    window = struct.pack('<iiii', 0, 0, width - 1, height - 1)
    header = struct.pack('<ii', 20000630, 2)
    header += attribute('channels', 'chlist', channels)
    header += attribute('compression', 'compression', b'\0')
    header += attribute('dataWindow', 'box2i', window)
    header += attribute('displayWindow', 'box2i', window)
    header += attribute('lineOrder', 'lineOrder', b'\0')
    header += attribute('pixelAspectRatio', 'float', struct.pack('<f', 1.0))
    header += attribute('screenWindowCenter', 'v2f', struct.pack('<ff', 0.0, 0.0))
    header += attribute('screenWindowWidth', 'float', struct.pack('<f', 1.0))
    header += b'\0'
    block = 8 + width * 3 * 2
    first = len(header) + height * 8
    with open(path, 'wb') as stream:
        stream.write(header)
        stream.write(struct.pack('<%dQ' % height, *[first + y * block for y in range(height)]))
        for y in range(height):
            stream.write(struct.pack('<ii', y, width * 3 * 2))
            for channel in (2, 1, 0):  # stored alphabetically: B, G, R
                stream.write(rgb[y, :, channel].astype('<f2').tobytes())


# ----------------------------------------------------------------------------------------- noise

def spectral_noise(size, beta, seed, low=1, high=None):
    """Tileable Gaussian noise with a 1/f^beta amplitude spectrum limited to [low, high] cycles per tile."""
    rng = np.random.default_rng(seed)
    fy = np.fft.fftfreq(size)[:, None] * size
    fx = np.fft.rfftfreq(size)[None, :] * size
    frequency = np.sqrt(fx * fx + fy * fy)
    amplitude = np.zeros_like(frequency)
    mask = frequency >= low
    if high is not None:
        mask &= frequency <= high
    amplitude[mask] = frequency[mask] ** (-beta)
    spectrum = amplitude * np.exp(2j * np.pi * rng.random(frequency.shape))
    noise = np.fft.irfft2(spectrum, s=(size, size))
    noise -= noise.mean()
    noise /= noise.std() + 1e-12
    return noise.astype(np.float32)


def blur(field, sigma):
    """Periodic Gaussian blur, sigma in texels."""
    height, width = field.shape
    fy = np.fft.fftfreq(height)[:, None]
    fx = np.fft.rfftfreq(width)[None, :]
    kernel = np.exp(-2 * (np.pi ** 2) * (sigma ** 2) * (fx ** 2 + fy ** 2))
    return np.fft.irfft2(np.fft.rfft2(field) * kernel, s=field.shape).astype(np.float32)


def equalize(field):
    """Rank transform to a uniform distribution on [0, 1].

    A uniform field makes threshold erosion predictable: `field > 1 - c` covers exactly the fraction c."""
    flat = field.ravel()
    order = np.argsort(flat, kind='stable')
    rank = np.empty(flat.size, dtype=np.int64)
    rank[order] = np.arange(flat.size)
    return (rank.astype(np.float32) / (flat.size - 1)).reshape(field.shape)


def voronoi(size, cells, seed, warp=None, jitter=0.9, chunk=512):
    """Tileable Voronoi diagram on a jittered grid.

    Returns (distance to the nearest cell edge, distance to the nearest site, random value of that site),
    with distances in cell units. warp: optional (dx, dy) displacement fields in cell units."""
    rng = np.random.default_rng(seed)
    sites = ((rng.random((cells, cells, 2)) - 0.5) * jitter + 0.5).astype(np.float32)
    values = rng.random((cells, cells)).astype(np.float32)
    edge = np.empty((size, size), np.float32)
    nearest = np.empty((size, size), np.float32)
    value = np.empty((size, size), np.float32)
    scale = cells / size
    for y0 in range(0, size, chunk):
        y1 = min(size, y0 + chunk)
        yy, xx = np.mgrid[y0:y1, 0:size].astype(np.float32)
        px = (xx + 0.5) * scale
        py = (yy + 0.5) * scale
        if warp is not None:
            px = px + warp[0][y0:y1]
            py = py + warp[1][y0:y1]
        ix = np.floor(px).astype(np.int64)
        iy = np.floor(py).astype(np.int64)
        fx = px - ix
        fy = py - iy
        best = np.full(px.shape, 1e9, np.float32)
        bx = np.zeros_like(px)
        by = np.zeros_like(px)
        bv = np.zeros_like(px)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                site = sites[(iy + dy) % cells, (ix + dx) % cells]
                rx = site[..., 0] + dx - fx
                ry = site[..., 1] + dy - fy
                distance = rx * rx + ry * ry
                closer = distance < best
                best = np.where(closer, distance, best)
                bx = np.where(closer, rx, bx)
                by = np.where(closer, ry, by)
                bv = np.where(closer, values[(iy + dy) % cells, (ix + dx) % cells], bv)
        # Exact distance to the cell border: project onto the bisector of the nearest site and each neighbour.
        border = np.full(px.shape, 1e9, np.float32)
        for dy in range(-2, 3):
            for dx in range(-2, 3):
                site = sites[(iy + dy) % cells, (ix + dx) % cells]
                rx = site[..., 0] + dx - fx
                ry = site[..., 1] + dy - fy
                ex = rx - bx
                ey = ry - by
                length = ex * ex + ey * ey
                valid = length > 1e-8
                inverse = 1.0 / np.sqrt(np.where(valid, length, 1))
                distance = (0.5 * (bx + rx) * ex + 0.5 * (by + ry) * ey) * inverse
                border = np.where(valid & (distance < border), distance, border)
        edge[y0:y1] = border
        nearest[y0:y1] = np.sqrt(best)
        value[y0:y1] = bv
    return edge, nearest, value
