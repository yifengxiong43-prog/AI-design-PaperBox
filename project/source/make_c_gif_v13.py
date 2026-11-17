from pathlib import Path

ROOT = Path(__file__).resolve().parent
src = (ROOT / 'make_c_gif_v12.py').read_text(encoding='utf-8')
src = src.replace('v12', 'v13')
start = src.index('for hinge in tray_hinges:')
end = src.index('\nif marker:', start)
src = src[:start] + "for hinge in tray_hinges:\n    # Small moved-out trays keep both side walls upright for the entire motion.\n    set_rot(hinge, (0, 0, 0), 1); set_rot(hinge, (0, 0, 0), 50)\n" + src[end:]
src = src.replace('⑤ 侧壁放平：从侧面直接取笔', '⑤ 小纸盘侧壁保持立起：从上方取笔')
src = src.replace('⑦ 侧壁立回，左右纸盘收回', '⑦ 小纸盘侧壁保持立起，左右纸盘收回')
exec(compile(src, str(ROOT / 'make_c_gif_v13.py'), 'exec'), {'__name__': '__main__', '__file__': str(ROOT / 'make_c_gif_v13.py')})
