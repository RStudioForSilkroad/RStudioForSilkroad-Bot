import os
import shutil
from PIL import Image

os.chdir('C:/Users/pc/New Era Online/NewEra-main')
frames = sorted(f for f in os.listdir('build/launcher-frames') if f.endswith('.png'))
os.makedirs('assets/launcher', exist_ok=True)
os.makedirs('assets/uniques', exist_ok=True)
for old in os.listdir('assets/launcher'):
    os.remove(os.path.join('assets/launcher', old))
for i, f in enumerate(frames):
    im = Image.open('build/launcher-frames/' + f).convert('RGB').resize((640, 360), Image.LANCZOS)
    im.save('assets/launcher/frame%02d.jpg' % i, quality=86, optimize=True)
print(len(frames), 'frames')
# the plate under the page list: the dragon's head, its fire and the gate (2:1)
src = Image.open('build/launcher-frames/f08.0.png').convert('RGB')
plate = src.crop((330, 20, 930, 320)).resize((384, 192), Image.LANCZOS)
plate.save('assets/launcher/plate.jpg', quality=88, optimize=True)
uniques = {
    'tiger': 'm_china_tigerwoman_z0.png', 'kerberos': 'm_europe_kerberos_z0.png', 'ivy': 'm_asiam_ivy_z0.png', 'uruchi': 'm_oasis_uruchi_z0.png',
    'isyutaru': 'm_karakoram_isyutaru_z0.png', 'yarkan': 'm_taklamakan_bonelord_z0.png', 'shaitan': 'm_roc_tahomet_z0.png'
}
for key, name in uniques.items():
    shutil.copy('C:/Users/pc/New Era Online/textures/droplog/' + name, 'assets/uniques/%s.png' % key)
total = sum(os.path.getsize('assets/launcher/' + f) for f in os.listdir('assets/launcher'))
print('launcher bytes', total)
sheet = Image.new('RGB', (7 * 100, 100), (40, 29, 23))
for i, key in enumerate(uniques):
    im = Image.open('assets/uniques/%s.png' % key).convert('RGB').resize((88, 88), Image.LANCZOS)
    sheet.paste(im, (i * 100 + 6, 6))
sheet.save('build/uniques-sheet.png')
