"""
Validation analysis for the 17 November 2026 field surveys (blog post 6).

Run from the repository root:   python3 Analysis/validate.py
Needs numpy and matplotlib. Prints every number quoted in post 6 and redraws
the 06-*.png charts in BlogPosts/images/.

What it does
  1. Loads every saved survey in Data/2026-11-17/surveys and drops duplicate
     saves (same skyline saved twice).
  2. Recomputes solar access from each saved skyline with a Python port of
     SolarEngine.cs + SolarAccessCalculator.cs and checks it against the
     number the phone saved.
  3. Compares repeat traces of the same spot, bin by bin.
  4. Rotates each skyline by a heading error and recomputes, to show what a
     wrong north does to the answer.
  5. Summarises compass error from every logged sun sighting.
"""
import csv, glob, hashlib, json, math, os, datetime as dt
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, 'Data')
IMG = os.path.join(ROOT, 'BlogPosts', 'images')

# ---------------------------------------------------------------- solar engine
# Line-for-line port of Assets/Scripts/Solar/SolarEngine.cs (NOAA algorithm).

D2R=math.pi/180; R2D=180/math.pi; EP=dt.datetime(1858,11,17)
def mod360(d): m=d%360.0; return m+360 if m<0 else m
def jc(t): return ((t-EP).total_seconds()/86400+2400000.5-2451545.0)/36525
def L0(t): return mod360(280.46646+t*(36000.76983+t*0.0003032))
def M(t): return 357.52911+t*(35999.05029-0.0001537*t)
def ecc(t): return 0.016708634-t*(0.000042037+0.0000001267*t)
def eqc(t):
    m=M(t)*D2R
    return math.sin(m)*(1.914602-t*(0.004817+0.000014*t))+math.sin(2*m)*(0.019993-0.000101*t)+math.sin(3*m)*0.000289
def app_long(t): return L0(t)+eqc(t)-0.00569-0.00478*math.sin((125.04-1934.136*t)*D2R)
def obl(t): return 23+(26+(21.448-t*(46.815+t*(0.00059-t*0.001813)))/60)/60+0.00256*math.cos((125.04-1934.136*t)*D2R)
def decl(x): t=jc(x); return math.asin(math.sin(obl(t)*D2R)*math.sin(app_long(t)*D2R))*R2D
def eot(x):
    t=jc(x); y=math.tan(obl(t)*D2R/2)**2; l0=L0(t)*D2R; m=M(t)*D2R; e=ecc(t)
    return 4*R2D*(y*math.sin(2*l0)-2*e*math.sin(m)+4*e*y*math.sin(m)*math.cos(2*l0)-0.5*y*y*math.sin(4*l0)-1.25*e*e*math.sin(2*m))
def refr(el):
    if el>85: return 0
    te=math.tan(el*D2R)
    if el>5: a=58.1/te-0.07/te**3+0.000086/te**5
    elif el>-0.575: a=1735+el*(-518.2+el*(103.4+el*(-12.79+el*0.711)))
    else: a=-20.772/te
    return a/3600
def position(lat,lon,x):
    d=decl(x)*D2R; tst=(x.hour*60+x.minute+x.second/60+eot(x)+4*lon)%1440
    ha=tst/4-180; la=lat*D2R
    cz=max(-1,min(1,math.sin(la)*math.sin(d)+math.cos(la)*math.cos(d)*math.cos(ha*D2R)))
    z=math.acos(cz); el=90-z*R2D; den=math.cos(la)*math.sin(z)
    c=max(-1,min(1,(math.sin(la)*cz-math.sin(d))/den)); acz=math.acos(c)*R2D
    az=mod360(acz+180) if ha>0 else mod360(540-acz)
    return az, el+refr(el)
def solar_noon(lon,date):
    mid=dt.datetime(date.year,date.month,date.day); m=720
    for _ in range(2): m=720-4*lon-eot(mid+dt.timedelta(minutes=m))
    return mid+dt.timedelta(minutes=m)

# ------------------------------------------------------------ solar access
_cache = {}
def sun_table(lat, lon, year=2026, step=5):
    """Every 5-minute sun position above the horizon for a year, with its
    clear-sky weight (Wh/m2), as SolarAccessCalculator.Compute does it."""
    key = (round(lat, 3), round(lon, 3))
    if key in _cache: return _cache[key]
    az, el, w, mo = [], [], [], []
    d = dt.datetime(year, 1, 1)
    while d.year == year:
        for m in range(0, 1440, step):
            a, e = position(lat, lon, d + dt.timedelta(minutes=m))
            if e <= 0: continue
            s = math.sin(e * D2R)
            am = 1 / (s + 0.50572 * (e + 6.07995) ** -1.6364)
            az.append(a); el.append(e); mo.append(d.month - 1)
            w.append(1353 * 0.7 ** (am ** 0.678) * s * step / 60)
        d += dt.timedelta(days=1)
    _cache[key] = tuple(map(np.array, (az, el, w, mo)))
    return _cache[key]

def elevation_at(h, az):
    """HorizonProfile.ElevationAt: linear between 1-degree bin centres."""
    x = np.mod(az, 360) - 0.5; x = np.where(x < 0, x + 360, x)
    i0 = np.floor(x).astype(int); f = x - i0
    return h[i0 % 360] * (1 - f) + h[(i0 + 1) % 360] * f

def access(h, lat, lon, heading_error=0.0):
    """Annual and monthly % of clear-sky sun that clears skyline h.
    heading_error rotates the whole skyline clockwise by that many degrees."""
    az, el, w, mo = sun_table(lat, lon)
    lit = el > elevation_at(h, az - heading_error)
    avail = np.bincount(mo, w, 12); recv = np.bincount(mo, w * lit, 12)
    return 100 * recv.sum() / avail.sum(), 100 * recv / avail

# ------------------------------------------------------------------ surveys
def load_surveys(folder):
    out = {}
    for f in sorted(glob.glob(os.path.join(folder, 'survey-*.json'))):
        d = json.load(open(f))
        h = np.array(d['horizonDeg'])
        out[d['id']] = dict(d, h=h, traced=np.array(d['binTraced']),
                            hash=hashlib.md5(np.round(h, 2).tobytes()).hexdigest()[:8])
    return out

# Which trace was taken where (see post 6 for how these were assigned).
SPOTS = {
    '20261117-093858': 'A',  '20261117-094311': 'A',
    '20261117-094511': 'A2', '20261117-094831': 'B',
    '20261117-095235': 'C1', '20261117-095345': 'C1',
    '20261117-095720': 'C2',
}

def main(charts=True):
    S = load_surveys(os.path.join(DATA, '2026-11-17', 'surveys'))
    print(f'{len(S)} saved files, {len({s["hash"] for s in S.values()})} distinct skylines\n')

    print('1. Recomputed vs saved on the phone')
    for k, spot in SPOTS.items():
        s = S[k]; a, m = access(s['h'], s['latitude'], s['longitude'])
        dm = np.abs(m - np.array(s['monthlyAccessPct'])).max()
        print(f'  {k} {spot:3} phone {s["annualAccessPct"]:6.2f}  python {a:6.2f}  worst month diff {dm:.3f}')

    print('\n2. Repeat traces of the same spot')
    arc = np.zeros(360, bool); arc[43:317] = True
    for a_id, b_id in [('20261117-093858', '20261117-094311'), ('20261117-095235', '20261117-095345')]:
        A, B = S[a_id], S[b_id]; both = A['traced'] & B['traced'] & arc
        d = (A['h'] - B['h'])[both]
        print(f'  {SPOTS[a_id]}: annual {A["annualAccessPct"]:.2f} vs {B["annualAccessPct"]:.2f}; '
              f'{both.sum()} common bins, median |diff| {np.median(np.abs(d)):.2f} deg, '
              f'90th pct {np.percentile(np.abs(d), 90):.2f}, max {np.abs(d).max():.2f}')
        print('     monthly diff', np.round(np.array(B['monthlyAccessPct']) - A['monthlyAccessPct'], 1))

    print('\n3. Heading error -> change in annual figure (points) [worst single month]')
    for k in ['20261117-094311', '20261117-094831', '20261117-095345', '20261117-095720']:
        s = S[k]; a0, m0 = access(s['h'], s['latitude'], s['longitude']); row = []
        for e in (-34, -11, -2, 2, 11, 34):
            a, m = access(s['h'], s['latitude'], s['longitude'], e)
            i = np.argmax(np.abs(m - m0)); row.append(f'{e:+}:{a - a0:+.1f}[{"JFMAMJJASOND"[i]}{m[i] - m0[i]:+.0f}]')
        print(f'  {SPOTS[k]:3}', ' '.join(row))

    c2 = S['20261117-095720']; h = c2['h'].copy(); h[43:84] = S['20261117-095345']['h'][43:84]
    print(f'\n  C2 if its untraced east (43-83 deg) were blocked like C1: {access(h, c2["latitude"], c2["longitude"])[0]:.1f} %')

    print('\n4. Compass error at each accepted sun sighting')
    events = {}
    for f in [os.path.join(DATA, '2026-11-15', 'calibrations.csv'), os.path.join(DATA, '2026-11-17', 'calibrations.csv')]:
        for r in csv.DictReader(open(f)): events[r['utc']] = r
    for s in S.values():
        for c in s.get('calibrations', []):
            events.setdefault(c['utc'], dict(utc=c['utc'], kind=c['kind'], accepted='1' if c['accepted'] else '0',
                offset_deg=c['offsetDeg'], compass_error_deg=c['compassErrorDeg'], latitude=c['latitude'], longitude=c['longitude']))
    groups = {}
    for e in events.values():
        if e['kind'] != 'sun' or e['accepted'] != '1' or float(e['compass_error_deg']) < -900: continue
        g = group(e); groups.setdefault(g, []).append(float(e['compass_error_deg']))
    for g in GROUP_ORDER:
        v = np.array(groups[g]); print(f'  {g:32} n={len(v):2}  mean {v.mean():+5.1f}  range {v.min():+5.1f} .. {v.max():+5.1f}')
    sun = [e for e in events.values() if e['kind'] == 'sun']
    print(f'  all sun sightings logged: {len(sun)}, accepted {sum(e["accepted"] == "1" for e in sun)}')
    rows = list(csv.DictReader(open(os.path.join(DATA, '2026-11-17', 'compass-from-video.csv'))))
    err = [float(r['compass_true_deg']) - float(r['sun_referenced_azimuth_deg']) for r in rows]
    print(f'  spot A sweep (read from the screen recording): {min(err):+.1f} .. {max(err):+.1f} over {len(err)} readings')
    if charts: draw(S, groups, rows)

GROUP_ORDER = ['15 Nov · indoors, at windows', '17 Nov · at a window', '16 Nov · rooftop',
               '17 Nov · roof terrace (C)', '17 Nov · open ground (A, B)']
def group(e):
    day, t = e['utc'][:10], e['utc'][11:16]
    if day == '2026-11-15': return GROUP_ORDER[0]
    if day == '2026-11-16': return GROUP_ORDER[2]
    if t < '09:20': return GROUP_ORDER[1]
    if float(e['latitude']) > 55.8624 or float(e['longitude']) > 9.8585: return GROUP_ORDER[4]
    return GROUP_ORDER[3]

# ------------------------------------------------------------------ charts
def draw(R, S, rows):
    import matplotlib; matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    LBL = {g: g for g in GROUP_ORDER}
    SURF='#fcfcfb'; INK='#1f1f1e'; INK2='#5c5c58'; GRID='#e4e4e0'
    C=['#2a78d6','#eb6834','#1baf7a','#eda100','#e87ba4']
    plt.rcParams.update({'font.family':'DejaVu Sans','font.size':11,'axes.edgecolor':GRID,'axes.labelcolor':INK2,
     'xtick.color':INK2,'ytick.color':INK2,'axes.facecolor':SURF,'figure.facecolor':SURF,'axes.grid':True,'grid.color':GRID,
     'grid.linewidth':0.8,'axes.spines.top':False,'axes.spines.right':False,'axes.titlecolor':INK,'axes.titlesize':12.5,'axes.titleweight':'bold','legend.frameon':False})
    OUT = IMG
    for r in R.values(): r.update(lat=r['latitude'], lon=r['longitude'], tr=r['traced'], annual=r['annualAccessPct'], monthly=r['monthlyAccessPct'])
    def sunpath(lat,lon,m,d):
        pts=[position(lat,lon,dt.datetime(2026,m,d)+dt.timedelta(minutes=k)) for k in range(0,1440,5)]
        return [p for p in pts if p[1]>0]
    def deg(ax): ax.xaxis.set_major_formatter(matplotlib.ticker.FuncFormatter(lambda v,_: f"{v:.0f}°"))
    # 1 repeat skylines
    fig,axs=plt.subplots(2,1,figsize=(9,7.2),sharex=True)
    for ax,(a,b,title,lab) in zip(axs,[('20261117-093858','20261117-094311','Spot A, open ground: two traces 4 minutes apart','A'),
                                   ('20261117-095235','20261117-095345','Spot C1, roof terrace beside a stairwell box: two traces 1 minute apart','C1')]):
        A,B=R[a],R[b]; x=np.arange(360)+0.5
        for (yy,mm,dd,nm) in [(0,6,21,'21 Jun'),(0,12,21,'21 Dec')]:
            p=sunpath(A['lat'],A['lon'],mm,dd); ax.plot([q[0] for q in p],[q[1] for q in p],color='#b4b4ae',lw=1.5,ls=(0,(4,3)),zorder=1)
            q=max(p,key=lambda q:q[1]); ax.text(q[0],q[1]+1.2,f"sun, {nm}",ha='center',color=INK2,fontsize=9.5)
        for r,c,t in [(A,C[0],a),(B,C[1],b)]:
            h=np.where(r['tr'],r['h'],np.nan); ax.plot(x,h,color=c,lw=2,label=f"{t[9:11]}:{t[11:13]} UTC trace → {r['annual']:.1f} % a year",zorder=3)
        ax.set_title(title,loc='left'); ax.set_ylim(0,62); ax.set_xlim(43,317); ax.set_ylabel('skyline elevation')
        ax.yaxis.set_major_formatter(matplotlib.ticker.FuncFormatter(lambda v,_: f"{v:.0f}°"))
        ax.legend(loc='upper right',fontsize=10)
    axs[1].set_xticks([45,90,135,180,225,270,315]); axs[1].set_xticklabels(['45° NE','90° E','135° SE','180° S','225° SW','270° W','315° NW'])
    axs[1].annotate('the box is 1–2 m away,\nso half a metre of hand\nmovement shifts it by degrees',xy=(118,36),xytext=(150,46),fontsize=9.5,color=INK2,arrowprops=dict(arrowstyle='-',color=INK2,lw=0.8))
    fig.tight_layout(); fig.savefig(f'{OUT}/06-repeat-skylines.png',dpi=160); plt.close(fig)

    # 2 compass error by place
    fig,ax=plt.subplots(figsize=(9,4.2))
    order=GROUP_ORDER
    ax.axvspan(-11,11,color='#2a78d6',alpha=0.08,lw=0); ax.text(0,-0.62,"iOS's own claim: ±11°",ha='center',va='center',color=C[0],fontsize=9.5)
    ax.axvline(0,color=INK2,lw=1)
    rng=np.random.default_rng(1)
    for i,k in enumerate(order):
        v=np.array(S[k]); y=i+rng.uniform(-0.17,0.17,len(v))
        ax.scatter(v,y,s=46,color=C[0] if k in GROUP_ORDER[3:] else C[1],edgecolor=SURF,linewidth=1.2,zorder=3)
        ax.text(62,i,f"n={len(v)}, mean {v.mean():+.1f}°",va='center',color=INK2,fontsize=9.5)
    ax.set_ylim(-0.85,len(order)-0.5); ax.set_yticks(range(len(order))); ax.set_yticklabels([LBL[k] for k in order]); ax.grid(axis='y',visible=False)
    ax.set_xlim(-25,85); ax.set_xticks(range(-20,61,10)); deg(ax)
    ax.set_xlabel('compass error at each sun sighting (compass − sun)')
    ax.set_title('The compass was fine in the open and badly off next to buildings',loc='left')
    fig.tight_layout(); fig.savefig(f'{OUT}/06-compass-by-place.png',dpi=160); plt.close(fig)

    # 3 compass vs bearing (video)
    az=np.array([float(r['sun_referenced_azimuth_deg']) for r in rows]); cp=np.array([float(r['compass_true_deg']) for r in rows])
    o=np.argsort(az); err=(cp-az)[o]; az=az[o]
    fig,ax=plt.subplots(figsize=(9,3.8))
    ax.axhline(0,color=INK2,lw=1); ax.plot(az,err,color=C[0],lw=2,zorder=2); ax.scatter(az,err,s=40,color=C[0],edgecolor=SURF,linewidth=1.2,zorder=3)
    ax.set_xlim(55,300); ax.set_ylim(-22,4); ax.set_xticks([90,135,180,225,270]); ax.set_xticklabels(['90° E','135° SE','180° S','225° SW','270° W'])
    ax.yaxis.set_major_formatter(matplotlib.ticker.FuncFormatter(lambda v,_: f"{v:.0f}°")); ax.set_ylabel('compass − truth')
    ax.set_title('Spot A, one sweep: the compass error depends on which way the phone faces',loc='left')
    fig.tight_layout(); fig.savefig(f'{OUT}/06-compass-vs-bearing.png',dpi=160); plt.close(fig)

    # 4 heading sensitivity
    shifts=np.arange(-40,41,1)
    fig,ax=plt.subplots(figsize=(9,4.6))
    ax.axvspan(-0.5,0.5,color=C[2],alpha=0.25,lw=0); ax.axvspan(-11,11,color='#2a78d6',alpha=0.06,lw=0)
    ax.text(0.8,1.6,'sun sighting ±0.5°',color='#137a55',fontsize=9.5); ax.text(-10.5,14.6,"compass, iOS's claim ±11°",color=C[0],fontsize=9.5)
    SP=[('20261117-094311','A, open ground',C[0]),('20261117-094831','B, courtyard',C[1]),('20261117-095345','C1, roof by the box',C[2]),('20261117-095720','C2, roof, open',C[3])]
    res={}
    for k,nm,c in SP:
        r=R[k]; a0,_=access(r['h'],r['lat'],r['lon']); d=[access(r['h'],r['lat'],r['lon'],s)[0]-a0 for s in shifts]; res[k]=d
        ax.plot(shifts,d,color=c,lw=2,label=f"{nm} ({a0:.1f} %)")
    ax.axhline(0,color=INK2,lw=1); ax.set_xlim(-40,40); ax.set_ylim(-12,16)
    ax.xaxis.set_major_formatter(matplotlib.ticker.FuncFormatter(lambda v,_: f"{v:+.0f}°" if v else "0°"))
    ax.set_xlabel('heading error (whole skyline rotated by this much)'); ax.set_ylabel('change in yearly figure, points')
    ax.set_title('What a wrong north does to the yearly answer',loc='left'); ax.legend(loc='lower left',fontsize=9.5,ncol=1)
    fig.tight_layout(); fig.savefig(f'{OUT}/06-heading-sensitivity.png',dpi=160); plt.close(fig)

    # 5 monthly by spot
    fig,ax=plt.subplots(figsize=(9,4.4))
    SP5=[('20261117-094311','A, open ground',C[0]),('20261117-094511','A2, 23 m from A',C[4]),('20261117-094831','B, courtyard',C[1]),('20261117-095345','C1, roof by the box',C[2]),('20261117-095720','C2, roof, open',C[3])]
    mx=np.arange(12)
    for k,nm,c in SP5:
        m=np.array(R[k]['monthly']); ax.plot(mx,m,color=c,lw=2,marker='o',ms=5,mec=SURF,label=f"{nm}: {R[k]['annual']:.1f} %")
    ax.set_xticks(mx); ax.set_xticklabels(list('JFMAMJJASOND')); ax.set_ylim(-3,103); ax.set_ylabel('share of clear-sky sun')
    ax.yaxis.set_major_formatter(matplotlib.ticker.FuncFormatter(lambda v,_: f"{v:.0f} %"))
    ax.set_title('Five spots within 250 m, month by month',loc='left'); ax.legend(loc='center left',bbox_to_anchor=(1.0,0.5),fontsize=9.5)
    fig.tight_layout(); fig.savefig(f'{OUT}/06-monthly-by-spot.png',dpi=160); plt.close(fig)

if __name__ == '__main__':
    main()
