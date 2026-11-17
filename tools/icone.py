import sys
from PIL import Image, ImageDraw
N=1024
def grad(c1,c2):
    g=Image.new('RGB',(N,N))
    d=ImageDraw.Draw(g)
    for y in range(N):
        t=y/(N-1)
        d.line([(0,y),(N,y)],fill=tuple(int(a+(b-a)*t) for a,b in zip(c1,c2)))
    return g
# Fond : carré arrondi sombre (couleurs du thème), liseré accent.
masque=Image.new('L',(N,N),0)
ImageDraw.Draw(masque).rounded_rectangle([24,24,N-24,N-24],radius=220,fill=255)
img=Image.new('RGBA',(N,N),(0,0,0,0))
img.paste(grad((0x2B,0x2F,0x37),(0x1A,0x1C,0x21)),(0,0),masque)
ImageDraw.Draw(img).rounded_rectangle([24,24,N-24,N-24],radius=220,outline=(0x3D,0x7B,0xEA,255),width=36)
# Éclair en dégradé accent.
pts=[(590,130),(300,560),(480,560),(410,894),(730,440),(540,440)]
m2=Image.new('L',(N,N),0)
ImageDraw.Draw(m2).polygon(pts,fill=255)
img.paste(grad((0x7F,0xAE,0xFF),(0x3D,0x7B,0xEA)),(0,0),m2)
tailles=[16,20,24,32,40,48,64,128,256]
img.resize((256,256),Image.LANCZOS).save(sys.argv[1],format='ICO',sizes=[(t,t) for t in tailles])
img.resize((512,512),Image.LANCZOS).save(sys.argv[2])
