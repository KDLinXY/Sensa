from PIL import Image, ImageDraw, ImageFont

size = 256
image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((8, 8, 248, 248), radius=58, fill="#11161B", outline="#293139", width=3)
draw.rounded_rectangle((42, 42, 214, 214), radius=48, fill="#5CE1B4")
try:
    font = ImageFont.truetype("arialbd.ttf", 132)
except OSError:
    font = ImageFont.load_default()
draw.text((128, 123), "S", font=font, fill="#10211C", anchor="mm")
image.save("assets/Sensa.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
image.save("assets/Sensa.png")
