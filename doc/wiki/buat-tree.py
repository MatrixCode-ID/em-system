#!/usr/bin/env python3
"""
buat-tree.py - pindai folder data/, lalu tulis tree.js dan konten.js di folder root.

Kembaran lintas-platform dari buat-tree.ps1. Di Windows pakai buka-wiki.cmd
(yang memanggil buat-tree.ps1) karena tidak butuh Python terpasang; berkas ini
untuk PC yang tidak punya PowerShell. Keluaran keduanya dibuat sama persis —
kalau salah satu diubah, ubah keduanya.

Pakai:
    python buat-tree.py            # pindai data/ di sebelah skrip ini
    python buat-tree.py /path/ke/proyek

Dua berkas yang dihasilkan, keduanya di folder root:
  tree.js    struktur folder untuk navigasi
  konten.js  isi setiap Index.html, siap disuntikkan app.html

Index.html di dalam data/ cukup berisi potongan body. Kerangka halamannya
(Bootstrap, ikon, tema) datang dari app.html, jadi tidak ada berkas hasil
rakitan yang mengotori folder data/.

Path relatif di dalam isi (misalnya ./pic.png) ditulis ulang menjadi path dari
folder root, karena isinya nanti tampil di dokumen app.html. Isi di dalam <pre>
dibiarkan apa adanya supaya contoh kode tidak ikut berubah.

Jalankan ulang setiap kali isi halaman atau susunan folder berubah.
"""
import json
import os
import posixpath
import re
import sys

# nama berkas yang dianggap sumber halaman sebuah folder, urut prioritas
KANDIDAT = ('index.html', 'Index.html', 'index.htm', 'default.html', 'README.html')
LEWATI = {'node_modules', '.git', '__pycache__', 'assets', 'css', 'js', 'img', 'images'}
SUMBER = 'data'        # folder berisi knowledge base, relatif terhadap proyek
NAMA_AKAR = 'Beranda'  # label folder data/ di navigasi
NAMA_PROYEK = 'html-knowledge'  # label akar di bilah atas app.html

# skema yang tidak boleh disentuh saat menulis ulang path
MUTLAK = ('http://', 'https://', '//', '#', '/', 'data:', 'mailto:', 'tel:', 'javascript:')

POLA_BODY = re.compile(r'<body[^>]*>(.*)</body>', re.S | re.I)
POLA_PRE = re.compile(r'(<pre\b.*?</pre>)', re.S | re.I)
POLA_URL = re.compile(r'\b(src|href|poster)\s*=\s*(["\'])([^"\']*)\2', re.I)


def baca(jalur):
    with open(jalur, encoding='utf-8') as f:
        return f.read()


def tulis(jalur, isi):
    with open(jalur, 'w', encoding='utf-8') as f:
        f.write(isi)


def sumber_halaman(folder):
    """Nama berkas sumber halaman di folder, atau None."""
    berkas = os.listdir(folder)
    for nama in KANDIDAT:
        if nama in berkas:
            return nama
    html_lain = sorted(f for f in berkas
                       if f.lower().endswith(('.html', '.htm')) and not f.startswith('_'))
    return html_lain[0] if html_lain else None


def ambil_body(isi):
    """Kalau sumbernya dokumen lengkap, ambil isi <body>-nya saja."""
    awal = isi.lstrip()[:200].lower()
    if awal.startswith('<!doctype') or awal.startswith('<html'):
        cocok = POLA_BODY.search(isi)
        if cocok:
            return cocok.group(1)
    return isi


def tulis_ulang_path(isi, prefiks):
    """Ubah path relatif jadi path dari folder root; <pre> dilewati."""
    def ganti(m):
        atribut, kutip, nilai = m.group(1), m.group(2), m.group(3)
        if not nilai or nilai.startswith(MUTLAK):
            return m.group(0)
        baru = posixpath.normpath(prefiks + '/' + nilai)
        return '%s=%s%s%s' % (atribut, kutip, baru, kutip)

    bagian = POLA_PRE.split(isi)
    return ''.join(b if b[:4].lower() == '<pre' else POLA_URL.sub(ganti, b) for b in bagian)


def isi_halaman(folder, relatif):
    """Kembalikan (nama sumber, isi siap pakai) untuk satu folder."""
    nama = sumber_halaman(folder)
    if nama is None:
        return None, None
    isi = tulis_ulang_path(ambil_body(baca(os.path.join(folder, nama))).strip(), relatif)
    return nama, isi


def rapikan(nama):
    return nama.replace('-', ' ').replace('_', ' ').strip()


def pindai(basis, relatif, konten):
    """Daftar node untuk subfolder di dalam basis/relatif.

    'relatif' selalu relatif terhadap folder proyek, jadi setiap path yang
    dihasilkan sudah berawalan 'data/'.
    """
    keluar = []
    penuh = os.path.join(basis, relatif)
    for nama in sorted(os.listdir(penuh)):
        jalur = os.path.join(penuh, nama)
        if not os.path.isdir(jalur) or nama.startswith('.') or nama in LEWATI:
            continue
        rel = (relatif + '/' + nama).replace(os.sep, '/')
        anak = pindai(basis, rel, konten)
        sumber, isi = isi_halaman(jalur, rel)
        if sumber is None and not anak:
            continue  # folder tanpa halaman dan tanpa isi: lewati
        if isi is not None:
            konten[rel] = isi
        keluar.append({
            'name': rapikan(nama),
            'path': rel,
            'page': sumber,
            'children': anak,
        })
    return keluar


def main():
    basis = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
    sumber_dir = os.path.join(basis, SUMBER)
    if not os.path.isdir(sumber_dir):
        sys.exit('Folder tidak ditemukan: ' + sumber_dir)

    konten = {}
    sumber, isi = isi_halaman(sumber_dir, SUMBER)
    if isi is not None:
        konten[SUMBER] = isi
    akar = {
        'name': NAMA_AKAR,
        'path': SUMBER,
        'page': sumber,
        'children': pindai(basis, SUMBER, konten),
    }
    if akar['page'] is None and not akar['children']:
        sys.exit('Tidak ada halaman HTML di ' + sumber_dir)

    tulis(os.path.join(basis, 'tree.js'),
          '// Dibuat otomatis oleh buat-tree.ps1 (atau buat-tree.py). Jangan disunting manual.\n'
          'const ROOT_NAME = ' + json.dumps(NAMA_PROYEK, ensure_ascii=False) + ';\n'
          'const TREE = ' + json.dumps([akar], ensure_ascii=False, indent=2) + ';\n')

    baris = ',\n'.join('  %s: %s' % (json.dumps(k, ensure_ascii=False), json.dumps(v, ensure_ascii=False))
                       for k, v in sorted(konten.items()))
    tulis(os.path.join(basis, 'konten.js'),
          '// Dibuat otomatis oleh buat-tree.ps1 (atau buat-tree.py). Jangan disunting manual.\n'
          '// Isi setiap Index.html di dalam ' + SUMBER + '/, disuntikkan oleh app.html.\n'
          'const KONTEN = {\n' + baris + '\n};\n')

    def jumlah(n):
        return sum(1 + jumlah(c['children']) for c in n)

    print('tree.js   : %d folder dari %s/' % (jumlah([akar]), SUMBER))
    print('konten.js : %d halaman' % len(konten))
    print('Buka app.html dengan klik dua kali.')


if __name__ == '__main__':
    main()
