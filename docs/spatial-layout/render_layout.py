"""Rebuild the design-only coordinate manifest and figures; requires matplotlib/numpy."""
from hall_design import objects, write_generated
from pathlib import Path
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle, Circle
from mpl_toolkits.mplot3d.art3d import Poly3DCollection

OUT = Path(__file__).resolve().parent
plt.rcParams.update({'font.family': ['Microsoft YaHei', 'DejaVu Sans'],
                     'axes.unicode_minus': False, 'font.size': 11,
                     'svg.hashsalt': 'upmix-spatial-layout-v1'})
BG, PANEL, INK, MUTED = '#091420', '#101f2e', '#edf4fa', '#9aafc0'
COLORS = {'direct': '#56d9d0', 'early': '#ffbf69', 'late': '#a392ff', 'reserve': '#748697'}
write_generated()

fig = plt.figure(figsize=(18,12), facecolor=BG)
fig.text(.05,.945,'UPMIX  /  虚拟音乐厅声源位置设计',color=INK,fontsize=25,weight='bold')
fig.text(.05,.909,'方案 B · 舞台直达声 + 侧向早期反射 + 六点混响包围    |    初始摄像机 C = (0, 1, −10)',color=MUTED,fontsize=13)
ax = fig.add_axes([.06,.21,.44,.65], facecolor=PANEL)
ax.set_title('01  俯视定位图  /  Unity 世界坐标',loc='left',pad=15,color=INK,fontsize=16)
ax.add_patch(Rectangle((-3.8,-6.8),7.6,4.1,facecolor='#173b45',edgecolor='#35717a',lw=1.2))
ax.text(0,-2.95,'概念舞台区域 · 非现有建筑模型',ha='center',color=MUTED,fontsize=10)
for radius in [2,4,6]:
    ax.add_patch(Circle((0,-10),radius,fill=False,ec='#304153',lw=.8,ls='--'))
offsets={'D1':(0,-22),'D2':(0,14),'D3':(0,-23),'D4':(5,17),'D5':(0,-23),'D6':(0,-23),'D7':(0,17),'D8':(0,13),
         'E1':(0,15),'E2':(0,15),'R1':(-2,16),'R2':(2,16),'R3':(0,-23),'R4':(0,-23),'R5':(-15,-24),'R6':(15,-24)}
for o in objects:
    x,y,z=o['world_xyz']; col=COLORS[o['layer']]
    if o['layer'] in ('early','late'):
        ax.plot([0,x],[-10,z],color=col,alpha=.3,lw=1,ls=':')
    ax.scatter(x,z,s=100 if o['layer']=='direct' else 75,c=col,marker='D' if o['layer']=='early' else ('s' if o['id'] in ('R5','R6') else 'o'),zorder=4)
    suffix=f'  ↑{y:g}m' if o['id'] in ('R5','R6') else ''
    ax.annotate(o['id']+' '+o['label']+suffix,(x,z),xytext=offsets[o['id']],textcoords='offset points',ha='center',color=col,fontsize=10)
ax.scatter(0,-10,c='#ffffff',s=110,marker='^',zorder=5)
ax.annotate('',(0,-8.8),(0,-9.8),arrowprops=dict(arrowstyle='->',color='white',lw=1.5))
ax.text(0,-10.55,'C 初始听音点',color=INK,ha='center',fontsize=10)
ax.set(xlim=(-5.3,5.3),ylim=(-14.3,-2.3),xlabel='X / m  ·  左 ← → 右',ylabel='Z / m  ·  朝向舞台为 +Z',aspect='equal')
ax.tick_params(colors=MUTED,labelsize=9)
ax.xaxis.label.set_color(MUTED); ax.yaxis.label.set_color(MUTED)
for s in ax.spines.values(): s.set_color('#304153')

a3=fig.add_axes([.51,.39,.45,.48],projection='3d',facecolor=BG)
a3.set_title('02  空间层次  /  斜俯视',loc='left',color=INK,fontsize=16,pad=12)
# Plot X, Z, Y to keep Unity height vertical.
floor=[(-4,-6.8,0),(4,-6.8,0),(4,-2.7,0),(-4,-2.7,0)]
a3.add_collection3d(Poly3DCollection([floor],facecolor='#245363',alpha=.55,edgecolor='#4b8090'))
for o in objects:
    x,y,z=o['world_xyz']; col=COLORS[o['layer']]
    a3.plot([x,x],[z,z],[0,y],color=col,alpha=.3,lw=1)
    a3.scatter([x],[z],[y],s=55,color=col,depthshade=False)
    label_height = y-.45 if o['id'] in ('E1','R6','D8') else y+.22
    a3.text(x,z,label_height,o['id'],color=col,fontsize=10,ha='center')
a3.scatter([0],[-10],[1],color='white',marker='^',s=100)
a3.text(0,-10,.35,'C',color=INK,ha='center')
a3.quiver(0,-10,1,0,1.4,0,color='white',arrow_length_ratio=.2)
a3.set(xlim=(-5,5),ylim=(-14,-2),zlim=(0,5),xlabel='X / m',ylabel='Z / m',zlabel='Y / m')
a3.view_init(elev=27,azim=-63)
a3.set_box_aspect((10,12,5))
for axis in [a3.xaxis,a3.yaxis,a3.zaxis]:
    axis.set_pane_color((.06,.12,.18,1)); axis.label.set_color(MUTED)
    axis._axinfo['grid']['color']=(.2,.3,.4,.35)
a3.tick_params(colors=MUTED,labelsize=8)

fig.text(.55,.34,'听感目标',color=INK,fontsize=16,weight='bold')
fig.text(.55,.305,'前方：主唱居中，钢琴 / 吉他展开，鼓组靠后',color=COLORS['direct'],fontsize=12)
fig.text(.55,.274,'侧前：E1 / E2 提供早期反射与舞台宽度',color=COLORS['early'],fontsize=12)
fig.text(.55,.243,'侧后及上方：R1—R6 仅输出混响，营造包围感',color=COLORS['late'],fontsize=12)
fig.text(.55,.211,'D8 器乐暂作备用；避免与拆分乐器重复叠加',color=MUTED,fontsize=11)
fig.text(.06,.135,'●  7 个直接音乐对象     ●  1 个器乐备用     ◆  2 个早期反射     ● / ■  6 个后期混响',color=INK,fontsize=13)
fig.text(.06,.094,'设计原则参考 Hold 等（2016）、Barron & Marshall（1981）及相关空间印象研究；具体坐标为本项目原创设计。',color=MUTED,fontsize=10)
fig.text(.06,.063,'本图为位置概念效果图，非 Unity 实机截图或声学仿真。单位 m；混响点为渲染方向代理，不代表实测墙面反射位置。',color=MUTED,fontsize=10)
fig.savefig(OUT/'spatial-layout-overview.png',dpi=160,facecolor=BG)
svg_path = OUT/'spatial-layout-overview.svg'
fig.savefig(svg_path,facecolor=BG,metadata={'Date': None})
svg_path.write_text('\n'.join(line.rstrip() for line in svg_path.read_text(encoding='utf-8').splitlines())+'\n',encoding='utf-8')
plt.close(fig)
print('Generated manifest, PNG and SVG in', OUT)
