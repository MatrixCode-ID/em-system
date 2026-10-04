<#
.SYNOPSIS
    buat-tree.ps1 - pindai folder data/, lalu tulis tree.js dan konten.js di folder root.

.DESCRIPTION
    Padanan PowerShell dari buat-tree.py, supaya generator bisa dijalankan di PC
    Windows mana pun tanpa memasang apa pun lebih dulu. Keluarannya dibuat sama
    persis dengan versi Python; kalau salah satu diubah, ubah keduanya.

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

.PARAMETER Proyek
    Folder proyek yang memuat data/, bukan folder data/-nya. Bila dikosongkan,
    dipakai folder tempat skrip ini berada.

.EXAMPLE
    .\buat-tree.ps1

.EXAMPLE
    .\buat-tree.ps1 C:\path\ke\proyek
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Proyek
)

$ErrorActionPreference = 'Stop'

# nama berkas yang dianggap sumber halaman sebuah folder, urut prioritas
$KANDIDAT = @('index.html', 'Index.html', 'index.htm', 'default.html', 'README.html')
$LEWATI = @('node_modules', '.git', '__pycache__', 'assets', 'css', 'js', 'img', 'images')
$SUMBER = 'data'                 # folder berisi knowledge base, relatif terhadap proyek
$NAMA_AKAR = 'Beranda'           # label folder data/ di navigasi
$NAMA_PROYEK = 'html-knowledge'  # label akar di bilah atas app.html

# skema yang tidak boleh disentuh saat menulis ulang path
$MUTLAK = @('http://', 'https://', '//', '#', '/', 'data:', 'mailto:', 'tel:', 'javascript:')

$POLA_BODY = New-Object System.Text.RegularExpressions.Regex('<body[^>]*>([\s\S]*)</body>', 'IgnoreCase')
$POLA_PRE = New-Object System.Text.RegularExpressions.Regex('(<pre\b[\s\S]*?</pre>)', 'IgnoreCase')
$POLA_URL = New-Object System.Text.RegularExpressions.Regex('\b(src|href|poster)\s*=\s*(["''])([^"'']*)\2', 'IgnoreCase')

$UTF8 = New-Object System.Text.UTF8Encoding($false)

function Baca([string]$jalur) {
    [System.IO.File]::ReadAllText($jalur, $UTF8)
}

function Tulis([string]$jalur, [string]$isi) {
    [System.IO.File]::WriteAllText($jalur, $isi, $UTF8)
}

# Urutkan berdasarkan kode karakter, bukan aturan bahasa, supaya hasilnya sama
# dengan sorted() di Python dan tidak ikut berubah mengikuti regional setting PC.
function Urutkan([string[]]$daftar) {
    $salinan = [string[]]$daftar
    if ($salinan.Length -gt 1) { [array]::Sort($salinan, [System.StringComparer]::Ordinal) }
    , $salinan
}

# Bikin literal string JSON. Ditulis sendiri, bukan ConvertTo-Json, karena
# ConvertTo-Json mengubah < dan > jadi \u003c sehingga isi HTML jadi sulit dibaca.
function JsonTeks([string]$teks) {
    $sb = New-Object System.Text.StringBuilder(($teks.Length + 16))
    [void]$sb.Append([char]0x22)
    foreach ($ch in $teks.ToCharArray()) {
        $kode = [int]$ch
        if ($kode -eq 0x22) { [void]$sb.Append('\"') }
        elseif ($kode -eq 0x5C) { [void]$sb.Append('\\') }
        elseif ($kode -eq 0x0A) { [void]$sb.Append('\n') }
        elseif ($kode -eq 0x0D) { [void]$sb.Append('\r') }
        elseif ($kode -eq 0x09) { [void]$sb.Append('\t') }
        elseif ($kode -eq 0x08) { [void]$sb.Append('\b') }
        elseif ($kode -eq 0x0C) { [void]$sb.Append('\f') }
        elseif ($kode -lt 0x20) { [void]$sb.Append(('\u{0:x4}' -f $kode)) }
        else { [void]$sb.Append($ch) }
    }
    [void]$sb.Append([char]0x22)
    $sb.ToString()
}

# Padanan posixpath.normpath: rapikan ./ dan ../ jadi satu path bersih.
function RapikanPath([string]$jalur) {
    $keping = New-Object System.Collections.Generic.List[string]
    foreach ($bagian in $jalur.Split('/')) {
        if ($bagian -eq '' -or $bagian -eq '.') { continue }
        if ($bagian -eq '..') {
            if ($keping.Count -gt 0 -and $keping[$keping.Count - 1] -ne '..') {
                $keping.RemoveAt($keping.Count - 1)
            }
            else { $keping.Add('..') }
            continue
        }
        $keping.Add($bagian)
    }
    if ($keping.Count -eq 0) { return '.' }
    $keping -join '/'
}

# Nama berkas dan folder di dalam sebuah folder, sudah terurut.
function IsiFolder([string]$folder) {
    $nama = New-Object System.Collections.Generic.List[string]
    foreach ($entri in [System.IO.Directory]::GetFileSystemEntries($folder)) {
        $nama.Add([System.IO.Path]::GetFileName($entri))
    }
    Urutkan $nama.ToArray()
}

# Nama berkas sumber halaman di folder, atau $null.
function SumberHalaman([string]$folder) {
    $berkas = IsiFolder $folder
    foreach ($nama in $KANDIDAT) {
        if ($berkas -ccontains $nama) { return $nama }
    }
    foreach ($nama in $berkas) {
        $kecil = $nama.ToLowerInvariant()
        if (($kecil.EndsWith('.html') -or $kecil.EndsWith('.htm')) -and -not $nama.StartsWith('_')) {
            return $nama
        }
    }
    $null
}

# Kalau sumbernya dokumen lengkap, ambil isi <body>-nya saja.
function AmbilBody([string]$isi) {
    $awal = $isi.TrimStart()
    if ($awal.Length -gt 200) { $awal = $awal.Substring(0, 200) }
    $awal = $awal.ToLowerInvariant()
    if ($awal.StartsWith('<!doctype') -or $awal.StartsWith('<html')) {
        $cocok = $POLA_BODY.Match($isi)
        if ($cocok.Success) { return $cocok.Groups[1].Value }
    }
    $isi
}

# Ubah path relatif jadi path dari folder root; isi <pre> dilewati.
function TulisUlangPath([string]$isi, [string]$prefiks) {
    $ganti = [System.Text.RegularExpressions.MatchEvaluator] {
        param($m)
        $nilai = $m.Groups[3].Value
        if ($nilai -eq '') { return $m.Value }
        foreach ($skema in $MUTLAK) {
            if ($nilai.StartsWith($skema)) { return $m.Value }
        }
        $kutip = $m.Groups[2].Value
        $m.Groups[1].Value + '=' + $kutip + (RapikanPath ($prefiks + '/' + $nilai)) + $kutip
    }

    $hasil = New-Object System.Text.StringBuilder
    foreach ($bagian in $POLA_PRE.Split($isi)) {
        $kepala = $bagian
        if ($kepala.Length -gt 4) { $kepala = $kepala.Substring(0, 4) }
        if ($kepala.ToLowerInvariant() -eq '<pre') { [void]$hasil.Append($bagian) }
        else { [void]$hasil.Append($POLA_URL.Replace($bagian, $ganti)) }
    }
    $hasil.ToString()
}

# Sumber dan isi siap pakai untuk satu folder.
function IsiHalaman([string]$folder, [string]$relatif) {
    $nama = SumberHalaman $folder
    if ($null -eq $nama) {
        return [pscustomobject]@{ Sumber = $null; Isi = $null }
    }
    $mentah = (AmbilBody (Baca (Join-Path $folder $nama))).Trim()
    [pscustomobject]@{ Sumber = $nama; Isi = (TulisUlangPath $mentah $relatif) }
}

function Rapikan([string]$nama) {
    $nama.Replace('-', ' ').Replace('_', ' ').Trim()
}

# Daftar node untuk subfolder di dalam basis/relatif. 'relatif' selalu relatif
# terhadap folder proyek, jadi setiap path yang dihasilkan sudah berawalan 'data/'.
function Pindai([string]$basis, [string]$relatif, [hashtable]$konten) {
    $keluar = New-Object System.Collections.Generic.List[object]
    $penuh = Join-Path $basis $relatif
    foreach ($nama in (IsiFolder $penuh)) {
        $jalur = Join-Path $penuh $nama
        if (-not [System.IO.Directory]::Exists($jalur)) { continue }
        if ($nama.StartsWith('.') -or ($LEWATI -contains $nama)) { continue }

        $rel = $relatif + '/' + $nama
        $anak = Pindai $basis $rel $konten
        $halaman = IsiHalaman $jalur $rel
        if ($null -eq $halaman.Sumber -and $anak.Count -eq 0) { continue }  # tanpa halaman & tanpa isi
        if ($null -ne $halaman.Isi) { $konten[$rel] = $halaman.Isi }

        $keluar.Add([pscustomobject]@{
                name     = Rapikan $nama
                path     = $rel
                page     = $halaman.Sumber
                children = $anak
            })
    }
    , $keluar
}

# Cetak satu node sebagai JSON dengan lekuk 2 spasi, sama seperti json.dumps.
function NodeKeJson($node, [int]$lekuk) {
    $p = ' ' * $lekuk
    $p2 = ' ' * ($lekuk + 2)
    $baris = New-Object System.Collections.Generic.List[string]
    $baris.Add($p + '{')
    $baris.Add($p2 + '"name": ' + (JsonTeks $node.name) + ',')
    $baris.Add($p2 + '"path": ' + (JsonTeks $node.path) + ',')
    if ($null -eq $node.page) { $baris.Add($p2 + '"page": null,') }
    else { $baris.Add($p2 + '"page": ' + (JsonTeks $node.page) + ',') }
    if ($node.children.Count -eq 0) {
        $baris.Add($p2 + '"children": []')
    }
    else {
        $baris.Add($p2 + '"children": [')
        $anak = New-Object System.Collections.Generic.List[string]
        foreach ($c in $node.children) { $anak.Add((NodeKeJson $c ($lekuk + 4))) }
        $baris.Add(($anak -join ",`n"))
        $baris.Add($p2 + ']')
    }
    $baris.Add($p + '}')
    $baris -join "`n"
}

function HitungFolder($daftar) {
    $jumlah = 0
    foreach ($n in $daftar) { $jumlah += 1 + (HitungFolder $n.children) }
    $jumlah
}

# ---------------- jalankan ----------------

if ($Proyek) { $basis = [System.IO.Path]::GetFullPath($Proyek) }
else { $basis = $PSScriptRoot }

$sumberDir = Join-Path $basis $SUMBER
if (-not [System.IO.Directory]::Exists($sumberDir)) {
    Write-Error ('Folder tidak ditemukan: ' + $sumberDir)
    exit 1
}

$konten = @{}
$halamanAkar = IsiHalaman $sumberDir $SUMBER
if ($null -ne $halamanAkar.Isi) { $konten[$SUMBER] = $halamanAkar.Isi }

$akar = [pscustomobject]@{
    name     = $NAMA_AKAR
    path     = $SUMBER
    page     = $halamanAkar.Sumber
    children = (Pindai $basis $SUMBER $konten)
}

if ($null -eq $akar.page -and $akar.children.Count -eq 0) {
    Write-Error ('Tidak ada halaman HTML di ' + $sumberDir)
    exit 1
}

Tulis (Join-Path $basis 'tree.js') (
    "// Dibuat otomatis oleh buat-tree.ps1 (atau buat-tree.py). Jangan disunting manual.`n" +
    'const ROOT_NAME = ' + (JsonTeks $NAMA_PROYEK) + ";`n" +
    "const TREE = [`n" + (NodeKeJson $akar 2) + "`n];`n")

$baris = New-Object System.Collections.Generic.List[string]
foreach ($kunci in (Urutkan ([string[]]$konten.Keys))) {
    $baris.Add('  ' + (JsonTeks $kunci) + ': ' + (JsonTeks $konten[$kunci]))
}

Tulis (Join-Path $basis 'konten.js') (
    "// Dibuat otomatis oleh buat-tree.ps1 (atau buat-tree.py). Jangan disunting manual.`n" +
    "// Isi setiap Index.html di dalam $SUMBER/, disuntikkan oleh app.html.`n" +
    "const KONTEN = {`n" + ($baris -join ",`n") + "`n};`n")

Write-Host ('tree.js   : {0} folder dari {1}/' -f (HitungFolder @($akar)), $SUMBER)
Write-Host ('konten.js : {0} halaman' -f $konten.Count)
Write-Host 'Selesai. Muat ulang app.html.'
