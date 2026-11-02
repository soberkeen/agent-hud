# -*- coding: utf-8 -*-
"""GPT-SoVITS api_v2 批量配音:13 句女声 -> remotion/public/audio/nar/*.wav (44.1k stereo)
用法: python gen_nar.py  (需先起 api_v2.py 于 127.0.0.1:9880)"""
import json, os, subprocess, sys, time, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
NAR_OUT = os.path.join(HERE, '..', '..', 'HDR 开关', 'tools', 'promo', 'remotion', 'public', 'audio', 'nar')
REF = os.path.join(HERE, 'ref_audio', 'ref_melo.wav')
PROMPT = 'Agent HUD 是一个钉在屏幕边缘的桌面挂件，额度余额一目了然，悬停就能看到更多细节，权限确认也只需要点一下。'

LINES = json.load(open(os.path.join(HERE, 'nar_lines.json'), encoding='utf-8'))

def tts(text, out_raw, retries=3):
    body = json.dumps({
        'text': text, 'text_lang': 'zh',
        'ref_audio_path': REF, 'prompt_text': PROMPT, 'prompt_lang': 'zh',
        'speed_factor': 1.0, 'media_type': 'wav', 'streaming_mode': False,
        'top_k': 15, 'top_p': 1.0, 'temperature': 1.0,
        'seed': -1,
    }).encode('utf-8')
    for a in range(retries):
        try:
            req = urllib.request.Request('http://127.0.0.1:9880/tts', data=body,
                                         headers={'Content-Type': 'application/json'})
            with urllib.request.urlopen(req, timeout=300) as r:
                data = r.read()
            if len(data) < 2000:
                raise RuntimeError('short response: %s' % data[:200])
            open(out_raw, 'wb').write(data)
            return True
        except urllib.error.HTTPError as e:
            detail = ''
            try:
                detail = e.read().decode('utf-8', 'replace')[:300]
            except Exception:
                pass
            print('retry', a, 'HTTP', e.code, detail, flush=True)
            time.sleep(6)
        except Exception as e:
            print('retry', a, type(e).__name__, str(e)[:120], flush=True)
            time.sleep(6)
    return False

def main():
    os.makedirs(NAR_OUT, exist_ok=True)
    only = sys.argv[1] if len(sys.argv) > 1 else None
    fail = []
    for item in LINES:
        key, text = item['key'], item['text']
        if only and key != only:
            continue
        raw = os.path.join(NAR_OUT, '_raw_' + key + '.wav')
        if not os.path.exists(raw) or os.path.getsize(raw) < 2000:
            if not tts(text, raw):
                fail.append(key); continue
        final = os.path.join(NAR_OUT, key + '.wav')
        # 响度归一(口播清晰度)+统一 44.1k stereo
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', raw,
                        '-af', 'loudnorm=I=-16:TP=-1.5:LRA=11',
                        '-ar', '44100', '-ac', '2', '-sample_fmt', 's16', final], check=True)
        d = subprocess.run(['ffprobe', '-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', final],
                           capture_output=True, text=True).stdout.strip()
        print(key, 'ok', d, flush=True)
    if fail:
        print('FAILED:', fail); sys.exit(1)
    print('ALL_DONE')

if __name__ == '__main__':
    main()
