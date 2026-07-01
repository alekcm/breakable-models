#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Единый мастер-скрипт для генерации всех 14 текстур персонажа Дианы (Diana Venicia).
Это единственный актуальный Python-скрипт, который вам нужен!
Он собирает все 14 файлов .png в папку Diana_Textures/.
"""

import os
import gc
import time
import numpy as np
from PIL import Image, ImageDraw

t_start = time.time()
OUTPUT_DIR = 'Diana_Textures'
os.makedirs(OUTPUT_DIR, exist_ok=True)
MODELS_DIR = 'breakable-models/Модели'

print("=== Запуск генерации всех 14 текстур Дианы ===")

# 1. Лицо (Diana_Face_Skin.png)
print("1. Генерация Diana_Face_Skin.png...")
im = Image.open(f'{MODELS_DIR}/Лицо 1.png').convert('RGBA')
arr = np.array(im, dtype=float)
alpha = arr[:, :, 3]
new_rgb = np.clip(arr[:, :, :3] + np.array([35.0, 48.0, 60.0]) * (alpha[:, :, None]/255.0), 0, 255)
out = np.zeros_like(arr)
out[:, :, :3] = new_rgb
out[:, :, 3] = alpha
Image.fromarray(out.astype(np.uint8)).save(f'{OUTPUT_DIR}/Diana_Face_Skin.png')
del im, arr, out; gc.collect()

# 2. Глаза 1 в 1 (Diana_Eyes.png)
print("2. Генерация Diana_Eyes.png...")
im_eyes = Image.open(f'{MODELS_DIR}/Глаза.png').convert('RGBA')
arr_eyes = np.array(im_eyes)
alpha_eyes = arr_eyes[:, :, 3]
out_eyes = np.zeros_like(arr_eyes)
centers = [(257.5, 260.0), (765.5, 260.0)]
h_e, w_e = arr_eyes.shape[:2]
Ye, Xe = np.ogrid[:h_e, :w_e]

for cx, cy in centers:
    dx = (Xe - cx) / 110.0
    dy = (Ye - cy) / 125.0
    r = np.sqrt(dx*dx + dy*dy)
    color = np.zeros((h_e, w_e, 3), dtype=float)
    c_top = np.array([35.0, 18.0, 36.0])
    c_mid = np.array([87.0, 38.0, 74.0])
    c_bot = np.array([239.0, 130.0, 164.0])
    
    mask_up = (dy < -0.1)[:, :, None]
    t_up = np.clip((dy[:, :, None] + 0.8) / 0.7, 0, 1)
    color = np.where(mask_up, c_top * (1 - t_up) + c_mid * t_up, c_mid)
    
    mask_low = (dy >= -0.1)[:, :, None]
    t_low = np.clip((dy[:, :, None] + 0.1) / 0.6, 0, 1)
    color = np.where(mask_low, c_mid * (1 - t_low) + c_bot * t_low, color)
    
    ring_r = np.sqrt((dx/0.50)**2 + ((dy-0.15)/0.50)**2)
    ring_band = np.exp(-((ring_r - 1.0)**2 / 0.010))
    ring_vis = np.clip((dy + 0.1) / 0.15, 0, 1) * ring_band
    color = color * (1 - ring_vis[:, :, None] * 0.75) + np.array([249.0, 139.0, 171.0]) * (ring_vis[:, :, None] * 0.75)
    
    pupil_r = np.sqrt((dx/0.28)**2 + (dy/0.32)**2)
    pupil_mask = np.clip(1.0 - (pupil_r - 0.75)/0.25, 0, 1)[:, :, None]
    color = color * (1 - pupil_mask) + np.array([20.0, 10.0, 15.0]) * pupil_mask
    
    rim = np.clip((r - 0.76) / 0.24, 0, 1)[:, :, None]
    color = color * (1 - rim) + np.array([20.0, 10.0, 15.0]) * rim
    
    h1 = np.exp(-(((dx + 0.24)/0.13)**2 + ((dy + 0.24)/0.13)**2))
    shine = np.clip(h1 * 1.7, 0, 1)[:, :, None]
    color = color * (1 - shine) + np.array([255.0, 245.0, 250.0]) * shine
    
    eye_reg = (r <= 1.05) & (alpha_eyes > 0)
    out_eyes[eye_reg, :3] = np.clip(color[eye_reg], 0, 255)
    out_eyes[eye_reg, 3] = alpha_eyes[eye_reg]

rem = (out_eyes[:, :, 3] == 0) & (alpha_eyes > 0)
if rem.any():
    out_eyes[rem] = arr_eyes[rem]
Image.fromarray(out_eyes.astype(np.uint8)).save(f'{OUTPUT_DIR}/Diana_Eyes.png')
del im_eyes, arr_eyes, out_eyes; gc.collect()

# 3. Брови (Diana_Eyebrows.png)
print("3. Генерация Diana_Eyebrows.png...")
im_brow = Image.open(f'{MODELS_DIR}/Брови.png').convert('RGBA')
arr_brow = np.array(im_brow, dtype=float)
alpha_brow = arr_brow[:, :, 3]
luma_brow = (arr_brow[:, :, 0]*0.299 + arr_brow[:, :, 1]*0.587 + arr_brow[:, :, 2]*0.114) / 75.0
out_brow = np.zeros_like(arr_brow)
out_brow[alpha_brow > 0, :3] = np.clip(np.array([139.0, 30.0, 63.0]) * luma_brow[alpha_brow > 0, None], 0, 255)
out_brow[:, :, 3] = alpha_brow
Image.fromarray(out_brow.astype(np.uint8)).save(f'{OUTPUT_DIR}/Diana_Eyebrows.png')
del im_brow, arr_brow, out_brow; gc.collect()

# 4. Ресницы 1 в 1 (Diana_Eyelashes.png)
print("4. Генерация Diana_Eyelashes.png...")
layer_path = '/home/user/uploads/layer.png' if os.path.exists('/home/user/uploads/layer.png') else f'{MODELS_DIR}/Ресницы 1.png'
im_lash = Image.open(layer_path).convert('RGBA')
arr_lash = np.array(im_lash, dtype=float)
out_lash = np.zeros_like(arr_lash)
out_lash[arr_lash[:, :, 3] > 15, :3] = np.array([15.0, 12.0, 20.0])
out_lash[arr_lash[:, :, 3] > 15, 3] = 255
im_lash_out = Image.fromarray(out_lash.astype(np.uint8))
draw_l = ImageDraw.Draw(im_lash_out)
left_spikes = [
    (115, 95, 95, 75, 130, 95), (130, 92, 112, 68, 148, 90),
    (150, 88, 135, 62, 168, 86), (170, 84, 158, 58, 188, 82),
    (190, 80, 182, 56, 210, 80), (212, 78, 206, 55, 230, 78),
    (235, 77, 232, 56, 252, 77)
]
for p in left_spikes:
    draw_l.polygon([(p[0], p[1]), (p[2], p[3]), (p[4], p[5])], fill=(15, 12, 20, 255))
right_spikes = [(1024 - p[4], p[5], 1024 - p[2], p[3], 1024 - p[0], p[1]) for p in left_spikes]
for p in right_spikes:
    draw_l.polygon([(p[0], p[1]), (p[2], p[3]), (p[4], p[5])], fill=(15, 12, 20, 255))
draw_l.polygon([(120, 115), (108, 132), (132, 118)], fill=(15, 12, 20, 255))
draw_l.polygon([(138, 116), (128, 134), (148, 118)], fill=(15, 12, 20, 255))
draw_l.polygon([(1024-132, 118), (1024-108, 132), (1024-120, 115)], fill=(15, 12, 20, 255))
draw_l.polygon([(1024-148, 118), (1024-128, 134), (1024-138, 116)], fill=(15, 12, 20, 255))
im_lash_out.save(f'{OUTPUT_DIR}/Diana_Eyelashes.png')
del im_lash, arr_lash, out_lash; gc.collect()

# 5. Нос Danganronpa Style (Diana_Nose.png)
print("5. Генерация Diana_Nose.png...")
out_nose = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0))
draw_n = ImageDraw.Draw(out_nose)
draw_n.line([(512, 641), (516, 659)], fill=(35, 30, 40, 230), width=4)
draw_n.line([(516, 659), (506, 663)], fill=(35, 30, 40, 230), width=3)
out_nose.save(f'{OUTPUT_DIR}/Diana_Nose.png')

# 6. Губы прозрачные (Diana_Lips.png)
print("6. Генерация Diana_Lips.png...")
Image.new('RGBA', (1024, 1024), (0, 0, 0, 0)).save(f'{OUTPUT_DIR}/Diana_Lips.png')

# 7. Кожа тела + Тату (Diana_Skin.png)
print("7. Генерация Diana_Skin.png...")
im_skin = Image.open(f'{MODELS_DIR}/Кожа.png').convert('RGBA')
arr_skin = np.array(im_skin, dtype=float)
alpha_skin = arr_skin[:, :, 3]
new_skin = np.clip(arr_skin[:, :, :3] + np.array([40.0, 52.0, 62.0]) * (alpha_skin[:, :, None]/255.0), 0, 255)
out_skin = np.zeros_like(arr_skin)
out_skin[:, :, :3] = new_skin
out_skin[:, :, 3] = alpha_skin
im_skin_out = Image.fromarray(out_skin.astype(np.uint8))
draw_s = ImageDraw.Draw(im_skin_out)
for y_off in [0, 45, 90]:
    draw_s.line([(1580, 680+y_off), (1960, 655+y_off)], fill=(80, 85, 95, 230), width=14)
    draw_s.line([(1580, 678+y_off), (1960, 653+y_off)], fill=(140, 145, 155, 230), width=4)
draw_s.polygon([(1900, 645), (1935, 635), (1945, 650), (1925, 663), (1900, 655)], fill=(80, 85, 95, 230))
im_skin_out.save(f'{OUTPUT_DIR}/Diana_Skin.png')
del im_skin, arr_skin, out_skin; gc.collect()

# 8-10. Послойная одежда: Рубашка, Жилет, Комбинированный
print("8-10. Генерация слоёв одежды...")
im_top = Image.open(f'{MODELS_DIR}/Школа А.png').convert('RGBA')
arr_top = np.array(im_top, dtype=float)
h_t, w_t = arr_top.shape[:2]
Yt, Xt = np.ogrid[:h_t, :w_t]
luma_t = np.clip((arr_top[:, :, 0]*0.299 + arr_top[:, :, 1]*0.587 + arr_top[:, :, 2]*0.114) / 215.0, 0.7, 1.15)

arr_shirt = np.zeros_like(arr_top)
sleeve_m = (arr_top[:, :, 3] > 10) & (Yt <= 450) & ((Xt < 600) | (Xt > 1448))
arr_shirt[sleeve_m, :3] = np.clip(np.array([248.0, 248.0, 250.0]) * luma_t[sleeve_m, None], 0, 255)
arr_shirt[sleeve_m, 3] = arr_top[sleeve_m, 3]
cuff_m = sleeve_m & (Yt >= 320)
arr_shirt[cuff_m, :3] = np.clip(np.array([212.0, 91.0, 122.0]) * luma_t[cuff_m, None], 0, 255)
arr_shirt[((Xt % 16) < 4) & cuff_m, :3] *= 0.85

collar_m = (arr_top[:, :, 3] > 10) & (Yt <= 450) & (Xt >= 600) & (Xt <= 1448)
arr_shirt[collar_m, :3] = np.clip(np.array([248.0, 248.0, 250.0]) * luma_t[collar_m, None], 0, 255)
arr_shirt[collar_m, 3] = arr_top[collar_m, 3]

torso_reg = np.broadcast_to((Yt > 450) & (Yt <= 1210) & (Xt >= 392) & (Xt <= 1656), (h_t, w_t))
v_grad = np.broadcast_to(np.clip(1.08 - ((np.arange(h_t)[:, None] - 450) / 760.0) * 0.18, 0.85, 1.1), (h_t, w_t))
arr_shirt[torso_reg, :3] = np.array([248.0, 248.0, 250.0]) * v_grad[torso_reg, None]
arr_shirt[torso_reg, 3] = 255
Image.fromarray(arr_shirt.astype(np.uint8)).save(f'{OUTPUT_DIR}/Diana_Shirt.png')

arr_vest = np.zeros_like(arr_top)
arr_vest[torso_reg, :3] = np.array([32.0, 42.0, 78.0]) * v_grad[torso_reg, None]
arr_vest[torso_reg, 3] = 255
vneck_cut = np.broadcast_to(((Yt >= 450) & (Yt <= 540) & (Xt >= 750) & (Xt <= 920)) | ((Yt >= 450) & (Yt <= 540) & (Xt >= 1128) & (Xt <= 1298)), (h_t, w_t))
arr_vest[torso_reg & vneck_cut, 3] = 0
vtrim = np.broadcast_to(((Yt >= 450) & (Yt <= 548) & (Xt >= 730) & (Xt <= 750)) | ((Yt >= 450) & (Yt <= 548) & (Xt >= 1298) & (Xt <= 1318)), (h_t, w_t))
arr_vest[torso_reg & vtrim, :3] = np.array([248.0, 248.0, 250.0])
hem_tr = np.broadcast_to((Yt >= 1150) & (Yt <= 1205), (h_t, w_t))
arr_vest[torso_reg & hem_tr, :3] = np.array([248.0, 248.0, 250.0])

im_vest = Image.fromarray(arr_vest.astype(np.uint8))
draw_v = ImageDraw.Draw(im_vest)
def draw_emblem(d, cx, cy, rad=42):
    d.ellipse([cx-rad, cy-rad, cx+rad, cy+rad], fill=(240, 242, 245, 255), outline=(20, 25, 45, 255), width=4)
    r = rad - 8
    d.pieslice([cx-r, cy-r, cx+r, cy+r], 180, 270, fill=(20, 25, 40, 255))
    d.pieslice([cx-r, cy-r, cx+r, cy+r], 0, 90, fill=(20, 25, 40, 255))
    d.pieslice([cx-r, cy-r, cx+r, cy+r], 270, 360, fill=(240, 242, 245, 255))
    d.pieslice([cx-r, cy-r, cx+r, cy+r], 90, 180, fill=(240, 242, 245, 255))
draw_emblem(draw_v, 560, 630)
draw_emblem(draw_v, 1480, 630)
im_vest.save(f'{OUTPUT_DIR}/Diana_Vest.png')

arr_combo = arr_shirt.copy()
mask_vest = np.array(im_vest)[:, :, 3] > 0
arr_combo[mask_vest] = np.array(im_vest)[mask_vest]
Image.fromarray(arr_combo.astype(np.uint8)).save(f'{OUTPUT_DIR}/Diana_School_Top.png')
del im_top, arr_top, arr_shirt, arr_vest, arr_combo, im_vest; gc.collect()

# 11. Бантик (Diana_Bow.png)
print("11. Генерация Diana_Bow.png...")
im_bow = Image.open(f'{MODELS_DIR}/Бантик.png').convert('RGBA')
arr_bow = np.array(im_bow, dtype=float)
luma_bow = np.clip((arr_bow[:, :, 0]*0.299 + arr_bow[:, :, 1]*0.587 + arr_bow[:, :, 2]*0.114) / 42.0, 0.5, 1.4)
out_bow = np.zeros_like(arr_bow)
out_bow[arr_bow[:, :, 3] > 0, :3] = np.clip(np.array([218.0, 138.0, 48.0]) * luma_bow[arr_bow[:, :, 3] > 0, None], 0, 255)
out_bow[:, :, 3] = arr_bow[:, :, 3]
Image.fromarray(out_bow.astype(np.uint8)).save(f'{OUTPUT_DIR}/Diana_Bow.png')
del im_bow, arr_bow, out_bow; gc.collect()

# 12. Юбка (Diana_Skirt.png)
print("12. Генерация Diana_Skirt.png...")
im_skirt = Image.open(f'{MODELS_DIR}/Юбка соло 3.png').convert('RGBA')
arr_skirt = np.array(im_skirt, dtype=float)
luma_s = np.clip((arr_skirt[:, :, 0]*0.299 + arr_skirt[:, :, 1]*0.587 + arr_skirt[:, :, 2]*0.114) / 180.0, 0.6, 1.3)
out_skirt = np.zeros_like(arr_skirt)
out_skirt[1100:2048, :, :3] = 210.0, 58.0, 94.0
mask_s = arr_skirt[:, :, 3] > 10
out_skirt[mask_s, :3] = np.clip(np.array([210.0, 58.0, 94.0]) * luma_s[mask_s, None], 0, 255)
out_skirt[1100:2048, :, 3] = 255
out_skirt[1820:1850, :, :3] = 248.0, 248.0, 250.0
out_skirt[1870:1900, :, :3] = 248.0, 248.0, 250.0
Image.fromarray(out_skirt.astype(np.uint8)).save(f'{OUTPUT_DIR}/Diana_Skirt.png')
del im_skirt, arr_skirt, out_skirt; gc.collect()

# 13. Чулки + Ремни (Diana_Socks.png)
print("13. Генерация Diana_Socks.png...")
im_sock = Image.open(f'{MODELS_DIR}/колготки.png').convert('RGBA')
arr_sock = np.array(im_sock, dtype=float)
hk, wk = arr_sock.shape[:2]
Yk, Xk = np.ogrid[:hk, :wk]
luma_k = np.clip((arr_sock[:, :, 0]*0.299 + arr_sock[:, :, 1]*0.587 + arr_sock[:, :, 2]*0.114) / 180.0, 0.4, 1.3)
out_sock = np.zeros_like(arr_sock)
mask_k = arr_sock[:, :, 3] > 10
strap = mask_k & (((Yk >= 1020) & (Yk <= 1048)) | ((Yk >= 1068) & (Yk <= 1096)))
out_sock[strap, :3] = np.clip(np.array([45.0, 48.0, 55.0]) * luma_k[strap, None], 0, 255)
out_sock[strap, 3] = arr_sock[strap, 3]

socks = mask_k & (Yk >= 1185)
sw1 = socks & (Yk >= 1185) & (Yk <= 1215)
sr = socks & (Yk >= 1225) & (Yk <= 1255)
sw2 = socks & (Yk >= 1265) & (Yk <= 1290)
snavy = socks & (Yk > 1290)

out_sock[sw1 | sw2, :3] = np.clip(np.array([248.0, 248.0, 250.0]) * luma_k[sw1 | sw2, None], 0, 255)
out_sock[sw1 | sw2, 3] = arr_sock[sw1 | sw2, 3]
out_sock[sr, :3] = np.clip(np.array([210.0, 58.0, 94.0]) * luma_k[sr, None], 0, 255)
out_sock[sr, 3] = arr_sock[sr, 3]
out_sock[snavy, :3] = np.clip(np.array([32.0, 42.0, 78.0]) * luma_k[snavy, None], 0, 255)
out_sock[snavy, 3] = arr_sock[snavy, 3]
Image.fromarray(out_sock.astype(np.uint8)).save(f'{OUTPUT_DIR}/Diana_Socks.png')
del im_sock, arr_sock, out_sock; gc.collect()

# 14. Ботильоны (Diana_Boots.png)
print("14. Генерация Diana_Boots.png...")
im_boot = Image.open(f'{MODELS_DIR}/Высокий ботинок.png').convert('RGBA')
arr_boot = np.array(im_boot, dtype=float)
hb, wb = arr_boot.shape[:2]
Yb, Xb = np.ogrid[:hb, :wb]
v_b = np.clip(1.1 - ((Yb - 200) / 800.0) * 0.3, 0.7, 1.2)
c_b = np.clip(1.0 - np.abs(Xb - wb/2) / (wb/2) * 0.2, 0.8, 1.0)
s_b = v_b * c_b
out_boot = np.zeros_like(arr_boot)
mask_b = arr_boot[:, :, 3] > 10
out_boot[mask_b, :3] = np.clip(np.array([32.0, 33.0, 36.0]) * s_b[mask_b, None], 0, 255)
out_boot[:, :, 3] = arr_boot[:, :, 3]
im_boot_out = Image.fromarray(out_boot.astype(np.uint8))
draw_b = ImageDraw.Draw(im_boot_out)
for zx in [88, 420]:
    draw_b.rectangle([zx-6, 230, zx+6, 740], fill=(22, 23, 26, 255))
    for y in range(235, 735, 8):
        draw_b.line([(zx-3, y), (zx+3, y)], fill=(170, 175, 185, 255), width=3)
    draw_b.rectangle([zx-5, 225, zx+5, 245], fill=(190, 195, 205, 255))
im_boot_out.save(f'{OUTPUT_DIR}/Diana_Boots.png')

print(f"=== Все 14 текстур успешно созданы за {time.time()-t_start:.2f} сек. ===")
