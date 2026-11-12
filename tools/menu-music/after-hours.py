# "After Hours": a mid-tempo synthwave groove for the team HQ menus, synthesised from scratch.
# Writes a 44.1 kHz stereo WAV: python3 tools/menu-music/after-hours.py out.wav (needs numpy). The game ships it as Resources/Music/after-hours.ogg, levelled to -18 LUFS.
import numpy as np, wave, sys
SR=44100; BPM=108; BEAT=60/BPM; BAR=4*BEAT
rng=np.random.default_rng(11)
sections=['intro']*8+['a']*8+['b']*8+['a']*8+['brk']*4+['b']*8+['b2']*8+['outro']*8
BARS=len(sections); N=int((BARS*BAR+4)*SR)
L=np.zeros(N); R=np.zeros(N); VERB=np.zeros(N); DUCK=np.ones(N)
def midi(m): return 440*2**((m-69)/12)
def t_of(n): return np.arange(n)/SR
def filt(x,cut,hp=False,order=4):
    X=np.fft.rfft(x); f=np.fft.rfftfreq(len(x),1/SR)+1e-9; r=(f/cut)**order
    return np.fft.irfft(X*(r/(1+r) if hp else 1/(1+r)),len(x))
def saw(f,t,ph=0.0):
    # band-limited-ish saw: sum of harmonics under 12 kHz
    x=np.zeros_like(t); k=1
    while f*k<12000 and k<40: x+=np.sin(2*np.pi*f*k*t+ph*k)/k; k+=1
    return x*0.6
def add(sig,start,pan=0.0,verb=0.0,gain=1.0,duck=True):
    i=int(start*SR); j=min(N,i+len(sig)); sig=sig[:j-i]*gain
    l=np.cos((pan+1)*np.pi/4); r=np.sin((pan+1)*np.pi/4)
    d=DUCK[i:j] if duck else 1
    L[i:j]+=sig*l*1.414*d; R[i:j]+=sig*r*1.414*d; VERB[i:j]+=sig*verb
def pad(notes,dur,vel=0.3):
    n=int((dur+1.0)*SR); t=t_of(n); out=np.zeros(n)
    for m in notes:
        f=midi(m)
        for det in (-0.09,-0.03,0.04,0.1):
            out+=saw(f*2**(det/12),t,rng.random()*6.28)
    out=filt(out,1400)
    env=np.minimum(1,t/0.35)*np.clip((dur+0.8-t)/0.8,0,1)
    return out*env*vel/len(notes)
def pluck(m,vel=0.3):
    n=int(0.5*SR); t=t_of(n); f=midi(m)
    x=saw(f,t)+0.5*np.sign(np.sin(2*np.pi*f*1.003*t))*0.4
    # decaying brightness: blend a dark and a bright copy
    bright=filt(x,4500); dark=filt(x,900); e=np.exp(-t*14)
    return (bright*e+dark*(1-e))*np.exp(-t*7)*np.minimum(1,t*800)*vel
def bass(m,dur,vel=0.5):
    n=int((dur+0.05)*SR); t=t_of(n); f=midi(m)
    x=filt(saw(f,t)+0.6*np.sin(2*np.pi*f*t),600+1400*0)
    env=np.minimum(1,t*500)*np.clip((dur-t)/0.03,0,1)*(0.7+0.3*np.exp(-t*10))
    return np.tanh(x*1.5)*env*vel
def kick():
    n=int(0.4*SR); t=t_of(n)
    ph=2*np.pi*np.cumsum(48+90*np.exp(-t*40))/SR
    return np.sin(ph)*np.exp(-t*8)
def snare():
    n=int(0.45*SR); t=t_of(n)
    return filt(rng.standard_normal(n),1800,hp=True)*np.exp(-t*9)*0.5+np.sin(2*np.pi*200*t)*np.exp(-t*30)*0.5
def hat(open_=False):
    n=int((0.22 if open_ else 0.05)*SR); t=t_of(n)
    return filt(rng.standard_normal(n),8000,hp=True)*np.exp(-t*(14 if open_ else 80))*0.3
K,S,H,HO=kick(),snare(),hat(),hat(True)
# sidechain-style duck on every beat where the kick plays
for bar,sec in enumerate(sections):
    if sec in('a','b','b2','outro'):
        for b in range(4):
            i=int((bar*BAR+b*BEAT)*SR); n=int(BEAT*SR); t=t_of(n)
            DUCK[i:i+n]=np.minimum(DUCK[i:i+n],1-0.45*np.exp(-t*9))
# Fmaj7 - G6 - Em7 - Am7 in C, with a lift to Dm9 - G in the second half of b
PROG=[([53,57,60,64],41),([55,59,62,64],43),([52,55,59,62],40),([57,60,64,67],45)]
LIFT=[([50,53,57,60,64],38),([55,59,62,65],43),([52,55,59,62],40),([57,60,64,67],45)]
def pos(bar,beat,six=0): return bar*BAR+beat*BEAT+six*BEAT/4
for bar,sec in enumerate(sections):
    notes,root=(LIFT if sec=='b2' and bar%8>=4 else PROG)[bar%4]
    last=bar==BARS-1
    add(pad(notes,BAR*(1.6 if last else 1.0),0.32 if sec!='brk' else 0.4),pos(bar,0),pan=0,verb=0.5)
    if sec in('a','b','b2','brk') or (sec=='intro' and bar>=4):
        arp=[notes[0]+12,notes[1]+12,notes[2]+12,notes[3]+12,notes[2]+12,notes[1]+12,notes[3]+12,notes[0]+24]
        for s in range(16):
            add(pluck(arp[s%8],0.17 if sec!='intro' else 0.12),pos(bar,s//4,s%4),pan=0.35*np.sin(s*0.8),verb=0.4)
    if sec in('a','b','b2','outro') and not (sec=='outro' and bar>=BARS-2):
        for e in range(8):
            add(bass(root+(12 if e%2 else 0),BEAT/2*0.85,0.45),pos(bar,e//2,2*(e%2)),duck=False)
        for b in range(4): add(K,pos(bar,b),gain=0.85,duck=False)
        add(S,pos(bar,1),verb=0.6,gain=0.5,duck=False); add(S,pos(bar,3),verb=0.6,gain=0.5,duck=False)
        for b in range(4):
            add(HO if b%2 else H,pos(bar,b,2),pan=0.3,gain=0.45,duck=False)
            add(H,pos(bar,b,1),pan=-0.3,gain=0.15,duck=False); add(H,pos(bar,b,3),pan=-0.3,gain=0.15,duck=False)
        if bar%8==7: [add(S,pos(bar,3,k),gain=0.15+0.08*k,verb=0.5,duck=False) for k in range(4)]
    if sec in('b','b2'):
        # lead: slow saw melody, one long phrase per two bars
        mel=[[72,71,67,0],[69,0,67,64],[72,74,76,0],[74,72,71,69]][(bar//2)%4] if bar%2==0 else [0,0,0,0]
        for b,m in enumerate(mel):
            if m:
                n=int(BEAT*1.1*SR); t=t_of(n); f=midi(m)
                v=1+0.005*np.sin(2*np.pi*5*t)*np.clip(t*4-0.5,0,1)
                ph=np.cumsum(f*v)/SR
                x=filt(saw(1,ph*1.0) if False else np.sum([np.sin(2*np.pi*k*ph)/k for k in range(1,12)],axis=0),2200)
                add(x*np.minimum(1,t*30)*np.clip((BEAT*1.1-t)/0.1,0,1)*0.11,pos(bar,b),pan=-0.1,verb=0.6)
ir_n=int(2.6*SR); t=t_of(ir_n)
irL=filt(rng.standard_normal(ir_n)*np.exp(-t*2.6),5500); irR=filt(rng.standard_normal(ir_n)*np.exp(-t*2.6),5500)
def conv(x,ir):
    m=len(x)+len(ir); n=1<<int(np.ceil(np.log2(m)))
    return np.fft.irfft(np.fft.rfft(x,n)*np.fft.rfft(ir,n),n)[:len(x)]
vin=filt(VERB,300,hp=True)
L+=conv(vin,irL)*0.045; R+=conv(vin,irR)*0.045
mix=np.stack([L,R]); mix/=np.max(np.abs(mix))/0.9
mix=np.tanh(mix*1.3)/np.tanh(1.3)
fade=int(3.5*SR); mix[:,-fade:]*=np.linspace(1,0,fade)
pcm=(mix.T*32767*0.95).astype('<i2')
with wave.open(sys.argv[1],'wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
