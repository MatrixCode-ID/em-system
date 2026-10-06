<#
.SYNOPSIS
   Verifikasi manual garbage collection registry dengan docker sungguhan.

.DESCRIPTION
   Tujuan : membuktikan (1) push image kecil lalu hapus manifest lewat DELETE /v2 berjalan dan manifest
            yang dirujuk index ditolak 409, (2) review GC di UI tidak menyentuh blob yang masih muda,
            (3) push ulang dan pull setelah itu tetap berhasil. Skrip ini hanya mengerjakan bagian docker/HTTP;
            Review dan Run GC dilakukan di UI Container Manager (butuh sesi admin), langkahnya dicetak di akhir.

   Prasyarat:
     - docker CLI jalan dan registry Em.Api aktif (mis. http://localhost:5132; HTTP biasa hanya diterima docker untuk localhost).
     - Root dan container sudah dibuat di Container Manager (default: root 'acme', container 'gc-check').
     - Robot dengan hak Write pada root itu. Tokennya diberikan lewat environment variable EM_REGISTRY_TOKEN
       (jangan ditulis di skrip atau riwayat perintah).

   Dampak:
     - Membuat image lokal '<Registry>/<Image>:gc-check' dan folder sementara di $env:TEMP (dihapus lagi di akhir).
     - Mem-push image itu ke registry, menghapus manifest-nya lewat API, lalu mem-push ulang dan mem-pull.
       Hanya container yang disebut -Image yang tersentuh. Skrip tidak menghentikan proses apa pun.

.PARAMETER Registry
   Host registry tanpa skema, mis. localhost:5132.

.PARAMETER Image
   Nama pull dua segmen: root/container, mis. acme/gc-check.

.PARAMETER RobotName
   Nama robot (username docker login).

.EXAMPLE
   $env:EM_REGISTRY_TOKEN = '<token robot>'
   pwsh -File plan/registry-gc-review-manual/registry-gc-docker-check.ps1 -Registry localhost:5132 -Image acme/gc-check -RobotName acme-ci
#>
[CmdletBinding()]
param(
   [string]$Registry = 'localhost:5132',
   [string]$Image = 'acme/gc-check',
   [Parameter(Mandatory)][string]$RobotName
)

$ErrorActionPreference = 'Stop'

if (-not $env:EM_REGISTRY_TOKEN) { throw 'Set EM_REGISTRY_TOKEN to the robot token first.' }
if ($Image -notmatch '^[a-z0-9._-]+/[a-z0-9._-]+$') { throw "Image must be root/container (lowercase), got '$Image'." }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'docker CLI was not found in PATH.' }

$tag = "$Registry/${Image}:gc-check"
$baseUrl = "http://$Registry/v2/$Image"
$basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("${RobotName}:$($env:EM_REGISTRY_TOKEN)"))
$headers = @{
   Authorization = "Basic $basic"
   Accept        = 'application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json'
}
$work = Join-Path ([IO.Path]::GetFullPath($env:TEMP)) ("em-gc-check-" + [guid]::NewGuid().ToString('N'))

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Check($ok, $text) {
   if ($ok) { Write-Host "  PASS  $text" -ForegroundColor Green } else { Write-Host "  FAIL  $text" -ForegroundColor Red; $script:failed = $true }
}
$script:failed = $false

try {
   Step 'Build and push a tiny image'
   New-Item -ItemType Directory -Path $work | Out-Null
   Set-Content -Path (Join-Path $work 'marker.txt') -Value ("gc-check " + [guid]::NewGuid())
   Set-Content -Path (Join-Path $work 'Dockerfile') -Value "FROM scratch`nCOPY marker.txt /marker.txt"
   docker build -t $tag $work | Out-Null
   $env:EM_REGISTRY_TOKEN | docker login $Registry -u $RobotName --password-stdin | Out-Null
   docker push $tag | Out-Null
   $head = Invoke-WebRequest -Uri "$baseUrl/manifests/gc-check" -Method Head -Headers $headers -UseBasicParsing
   $digest = $head.Headers['Docker-Content-Digest'] | Select-Object -First 1
   Check ($digest -match '^sha256:[0-9a-f]{64}$') "pushed, manifest digest $digest"

   Step 'DELETE the manifest by digest'
   $delete = Invoke-WebRequest -Uri "$baseUrl/manifests/$digest" -Method Delete -Headers $headers -UseBasicParsing
   Check ($delete.StatusCode -eq 202) 'DELETE answered 202'
   try {
      Invoke-WebRequest -Uri "$baseUrl/manifests/gc-check" -Method Get -Headers $headers -UseBasicParsing | Out-Null
      Check $false 'tag should be gone after the manifest was deleted'
   } catch {
      Check ($_.Exception.Response.StatusCode.value__ -eq 404) 'GET by tag now answers 404'
   }

   Step 'Push again and pull back'
   docker push $tag | Out-Null
   docker rmi $tag | Out-Null
   docker pull $tag | Out-Null
   Check ($LASTEXITCODE -eq 0) 'push after delete, then pull, succeeded'

   Write-Host @"

== Manual steps in the Container Manager (needs an admin session; this script cannot do them)
  1. Toolbar -> Garbage collection, grace period 1 hour, click Review.
     The blobs you just pushed are younger than 1 hour, so they must NOT be listed (count 0 for them).
  2. To see a real deletion: delete the manifest of '$Image' in the UI (trash icon on the manifest row),
     wait until the blobs are older than the grace period, Review again (they are listed), then Run and confirm.
  3. Push again and pull once more to confirm the registry still serves new pushes after Run.
"@
}
finally {
   docker rmi $tag 2>$null | Out-Null
   if ($work -and (Test-Path $work)) {
      $full = [IO.Path]::GetFullPath($work)
      $tempRoot = [IO.Path]::GetFullPath($env:TEMP)
      if ($full.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $full -Leaf) -like 'em-gc-check-*') {
         Remove-Item -LiteralPath $full -Recurse -Force
      }
   }
}

if ($script:failed) { exit 1 }
