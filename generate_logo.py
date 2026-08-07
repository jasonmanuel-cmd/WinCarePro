#!/usr/bin/env python3
"""Generate a high-quality, professional WinCare Pro logo and icon assets."""
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

SIZE = 512
HR = SIZE * 4  # 2048 for internal rendering, then downsample for crispness
S = 4  # Scale factor

img = Image.new('RGBA', (HR, HR), (255, 255, 255, 0))
draw = ImageDraw.Draw(img)

# === DRAW SHIELD ===
# Outer shield - deep blue
shield_outer = [(256*S, 60*S), (400*S, 100*S), (400*S, 240*S), (320*S, 440*S), (192*S, 440*S), (112*S, 240*S), (112*S, 100*S)]
draw.polygon(shield_outer, fill=(26, 115, 232, 255), outline=(21, 87, 176, 255), width=8*S)

# Inner shield - lighter blue
inner_points = [
    (256*S, 75*S),
    (390*S, 115*S),
    (390*S, 235*S),
    (315*S, 430*S),
    (207*S, 430*S),
    (122*S, 235*S),
    (122*S, 115*S),
]
draw.polygon(inner_points, fill=(66, 133, 244, 255), outline=(26, 115, 232, 255), width=4*S)

# === HIGHLIGHT ===
# Subtle highlight on shield edge
highlight_points = [
    (256*S, 60*S),
    (400*S, 100*S),
    (400*S, 240*S),
]
draw.polygon(highlight_points, outline=(255, 255, 255, 60), width=4*S)

# === CHECKMARK ===
# White checkmark - crisp, well-positioned
# Stroke 1: going up from bottom-left
draw.line([(180*S, 305*S), (245*S, 380*S)], fill=(255, 255, 255, 255), width=26*S, joint='curve')
# Stroke 2: going down-right
draw.line([(245*S, 380*S), (350*S, 245*S)], fill=(255, 255, 255, 255), width=26*S, joint='curve')

# === GLOW BEHIND CHECKMARK ===
glow = Image.new('RGBA', (HR, HR), (0, 0, 0, 0))
glow_draw = ImageDraw.Draw(glow)
glow_draw.line([(185*S, 310*S), (250*S, 385*S)], fill=(100, 200, 255, 140), width=36*S, joint='curve')
glow_draw.line([(250*S, 385*S), (355*S, 250*S)], fill=(100, 200, 255, 140), width=36*S, joint='curve')
glow = glow.filter(ImageFilter.GaussianBlur(radius=10*S))
img = Image.alpha_composite(img, glow)
draw = ImageDraw.Draw(img)

# === TEXT ===
font_paths = [
    'C:/Windows/Fonts/segoeuib.ttf',
    'C:/Windows/Fonts/segoeui.ttf',
    'C:/Windows/Fonts/arialbd.ttf',
    'C:/Windows/Fonts/arial.ttf',
]

def get_font(size):
    for fp in font_paths:
        if os.path.exists(fp):
            try:
                return ImageFont.truetype(fp, size)
            except Exception:
                pass
    return ImageFont.load_default()

# "WinCare Pro" - all in white for high contrast against the blue shield area,
# or dark blue if over white background
# Let's position text below shield, in dark blue
font_brand = get_font(100)
text = 'WinCare Pro'
bbox = draw.textbbox((0, 0), text, font=font_brand)
text_w = bbox[2] - bbox[0]
text_y = int(0.73 * SIZE) * S
draw.text(((SIZE - text_w) / 2 * S, text_y), text, fill=(26, 115, 232, 255), font=font_brand)

# "Windows. Verified." slogan - dark gray, smaller
font_slogan = get_font(42)
slogan = 'Windows. Verified.'
bbox2 = draw.textbbox((0, 0), slogan, font=font_slogan)
text_w2 = bbox2[2] - bbox2[0]
slogan_y = int(0.83 * SIZE) * S
draw.text(((SIZE - text_w2) / 2 * S, slogan_y), slogan, fill=(50, 50, 50, 255), font=font_slogan)

# === DOWNSAMPLE ===
final = img.resize((SIZE, SIZE), Image.LANCZOS)

# Ensure assets dir exists
os.makedirs('assets', exist_ok=True)

# Save full-size logo
final.save('assets/logo-512.png', 'PNG', optimize=True)
print('Saved assets/logo-512.png')

# Save all standard icon sizes
for s in [16, 32, 48, 64, 128, 256]:
    resized = final.resize((s, s), Image.LANCZOS)
    resized.save(f'assets/logo-{s}.png', 'PNG', optimize=True)
    print(f'Saved assets/logo-{s}.png')

# Generate Windows ICO (multi-resolution)
img_256 = final.resize((256, 256), Image.LANCZOS).convert('RGBA')
img_256.save(
    'assets/WinCarePro.ico',
    format='ICO',
    sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
)
print('Saved assets/WinCarePro.ico')

# icon.png for PyInstaller
img_256_simple = final.resize((256, 256), Image.LANCZOS)
img_256_simple.save('assets/icon.png', 'PNG', optimize=True)
print('Saved assets/icon.png')

# Also save a white-logo variant (just the shield+checkmark) for use on dark backgrounds
# This is the "icon-only" version
icon_only = Image.new('RGBA', (HR, HR), (0, 0, 0, 0))
icon_draw = ImageDraw.Draw(icon_only)
icon_draw.polygon(shield_outer, fill=(26, 115, 232, 255), outline=(21, 87, 176, 255), width=8*S)
icon_draw.polygon(inner_points, fill=(66, 133, 244, 255), outline=(26, 115, 232, 255), width=4*S)
icon_draw.line([(180*S, 305*S), (245*S, 380*S)], fill=(255, 255, 255, 255), width=26*S, joint='curve')
icon_draw.line([(245*S, 380*S), (350*S, 245*S)], fill=(255, 255, 255, 255), width=26*S, joint='curve')
icon_final = icon_only.resize((SIZE, SIZE), Image.LANCZOS)
icon_final.save('assets/icon-only-256.png', 'PNG', optimize=True)
print('Saved assets/icon-only-256.png')

print('\nAll WinCare Pro logo and icon assets generated successfully!')
