"""Prepare matching transparent theme assets from the supplied logo reference."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
SOURCE = HERE / 'kapibara-reference.png'
original = Image.open(SOURCE).convert('RGB')
rgb = np.asarray(original).astype(float)
minimum = rgb.min(axis=2)
background = minimum > 235
near_background = np.asarray(Image.fromarray((background * 255).astype('uint8')).filter(ImageFilter.MaxFilter(5))) > 0
local_minimum = np.asarray(Image.fromarray(minimum.astype('uint8')).filter(ImageFilter.MinFilter(5))).astype(float)
alpha = np.ones(minimum.shape)
edge = near_background & ~background
alpha[edge] = np.clip((255 - minimum[edge]) / np.maximum(255 - local_minimum[edge], 1), 0, 1)
alpha[background] = 0
foreground = np.clip((rgb - 255 * (1-alpha[...,None])) / np.maximum(alpha[...,None], 1/255), 0, 255)
y, x = np.indices(minimum.shape)
text = (x >= 410) & (y >= 565) & (alpha > 0)
bold = text & (x < 1033)
thin = text & ~bold
for theme, bold_color, thin_color, backdrop in [
    ('light', (8,20,63), (58,76,118), (255,255,255)),
    ('dark', (244,247,255), (193,206,229), (16,24,39)),
]:
    pixels = foreground.copy()
    pixels[bold] = bold_color
    pixels[thin] = thin_color
    rgba = np.dstack((pixels, alpha*255)).round().astype('uint8')
    asset = Image.fromarray(rgba)
    asset.save(HERE / f'kapibara-logo-{theme}.png')
    preview = Image.new('RGBA', asset.size, backdrop+(255,))
    preview.alpha_composite(asset)
    preview.convert('RGB').save(HERE / f'kapibara-preview-{theme}.png')
    print(theme, asset.size, asset.mode, 'bounds:', asset.getbbox(), 'alpha:', asset.getchannel('A').getextrema())

light = Image.open(HERE / 'kapibara-logo-light.png')
dark = Image.open(HERE / 'kapibara-logo-dark.png')
assert light.size == dark.size == (1448,1086)
assert np.array_equal(np.asarray(light.getchannel('A')), np.asarray(dark.getchannel('A')))
assert np.array_equal(np.asarray(light)[:,:410], np.asarray(dark)[:,:410])
assert light.getchannel('A').getextrema() == (0,255)
print('Validated: matching dimensions, alpha masks and symbol; genuine transparency.')
