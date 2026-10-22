"""One-time, UI-only migration to the requested charcoal/coral palette."""
from pathlib import Path
import re,shutil
root=Path(__file__).resolve().parents[1]
paths=[root/'src'/n for n in ['App.xaml','MainWindow.xaml','SceneEditorWindow.xaml','MainWindow.xaml.cs','MainWindow.Scenes.cs','SourceEditorWindow.cs','QuickDock.cs','SceneEditorWindow.xaml.cs','Motion.cs'] if (root/'src'/n).exists()]
backup=root/'artifacts/design-0.4.1/before';backup.mkdir(parents=True,exist_ok=True)
for p in paths:
    if not (backup/p.name).exists():shutil.copy2(p,backup/p.name)
colors={'#09090B':'#101113','#121215':'#181A1E','#1C1C20':'#202329','#2C2C31':'#30343B','#A8A8B2':'#A6ABB4','#D62C3C':'#EF4965','#FF7A87':'#FF8095','#291216':'#33232B','#FF99A3':'#FF8095',
 '#202025':'#202329','#707079':'#6B7280','#F04B5A':'#EF4965','#1A1A1E':'#181A1E','#24242B':'#292D35','#35171E':'#33232B','#0D0D10':'#181A1E','#B9B9C3':'#A6ABB4','#505059':'#30343B','#36363E':'#30343B','#44444D':'#555C68','#D5D5DF':'#F5F5F7','#953544':'#8B3044','#3E3E47':'#30343B','#35353D':'#30343B','#55555F':'#555C68','#25252B':'#202329','#50505B':'#30343B',
 '#34343B':'#30343B','#0E0E11':'#101113','#101013':'#181A1E','#08080A':'#101113','#28282E':'#30343B','#6C3540':'#555C68','#C1C1CA':'#F5F5F7','#E6101014':'#EE181A1E','#1B1013':'#181A1E','#432129':'#30343B','#0B0B0E':'#181A1E','#69303C':'#30343B','#31151D':'#202329','#151519':'#202329','#37373F':'#30343B','#404047':'#30343B','#050507':'#101113','#15151A':'#181A1E','#D90B0B0E':'#EE181A1E','#C4C4CE':'#A6ABB4'}
for p in paths:
    s=p.read_text(encoding='utf-8-sig')
    s=re.sub(r'#[0-9A-Fa-f]{6,8}',lambda m:colors.get(m[0].upper(),m[0]),s)
    if p.suffix=='.xaml':
        # Small supporting copy gains 1–2px; main headings retain their scale.
        s=re.sub(r'FontSize="(9|10|11|12|13)"',lambda m:f'FontSize="{ {9:11,10:12,11:13,12:14,13:14}[int(m[1])] }"',s)
        s=s.replace('FontWeight="SemiBold"','FontWeight="Bold"')
        s=s.replace('CornerRadius="6"','CornerRadius="9"').replace('CornerRadius="5"','CornerRadius="9"')
        for old,new in {'SOURCE':'Source','DISPLAY':'Display','RECORDING SETUP':'Recording setup','AUDIO':'Audio','OUTPUT':'Output','YOUR SCENES':'Your scenes','SAVE &amp; CAPTURE':'Save &amp; capture','SHORTCUTS':'Shortcuts','PREVIEW OFF · SAVING RESOURCES':'Preview off · saving resources','LAYOUT MAP · DRAG TO POSITION':'Layout map · drag to position','LAYERS · TOP ITEM IS IN FRONT':'Layers · top item is in front'}.items():s=s.replace('Text="'+old+'"','Text="'+new+'"')
        s=s.replace('LineHeight="14"','LineHeight="18"')
    if p.name in ['SourceEditorWindow.cs','QuickDock.cs']:
        s=re.sub(r'FontSize\s*=\s*(10|11|12)(?!\d)',lambda m:'FontSize = '+str({10:12,11:13,12:14}[int(m[1])]),s)
        s=s.replace('FontWeights.SemiBold','FontWeights.Bold')
    p.write_text(s,encoding='utf-8')
