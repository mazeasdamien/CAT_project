
$path = "c:\Users\Damien Mazeas\Desktop\IFAC_Project\CAT_wpf_app\app_icon.ico"
Add-Type -AssemblyName System.Drawing

# Create a bitmap
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

# Background (Transparent or Dark)
# For icon, transparency is good, but let's use a dark rounded square for visibility
$rect = New-Object System.Drawing.Rectangle 10, 10, 236, 236
$brushBg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(30, 30, 30))
$g.FillEllipse($brushBg, $rect)

# Neon Blue Ring
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(0, 255, 255), 20)
$g.DrawEllipse($pen, 50, 50, 156, 156)

# Center Dot
$brushFg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0, 255, 255))
$g.FillEllipse($brushFg, 100, 100, 56, 56)

# Save as Icon
# Note: GetHicon creates a handle to an icon. 
# To get a high quality icon with multiple sizes is harder in pure PS without external libs,
# but this will create a valid basic icon.
$hIcon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)

$fileStream = New-Object System.IO.FileStream($path, [System.IO.FileMode]::Create)
$icon.Save($fileStream)
$fileStream.Close()

$g.Dispose()
$bmp.Dispose()
