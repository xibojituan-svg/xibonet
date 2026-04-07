# 通用聊天记录拉取工具
# 用法: python fetch_chat.py <UID>
import urllib.request, json, os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

def fetch_chat(uid):
    output_dir = os.path.join('d:', os.sep, 'xibo2026data', '聊天记录')
    os.makedirs(output_dir, exist_ok=True)

    headers = {'Content-Type': 'application/json'}
    time_range = {'startTime': '2024-01-01 00:00:00', 'endTime': '2026-12-31 23:59:59'}

    # 1. 拉取私聊
    payload = json.dumps({'studentUid': uid, **time_range}).encode('utf-8')
    try:
        req = urllib.request.Request('https://ops.xibojiaoyu.com/xmkp-backend-middle/ops/qwChat/queryChatByStudent', data=payload, headers=headers)
        res = urllib.request.urlopen(req, timeout=20)
        private_data = json.loads(res.read().decode('utf-8'))
        print(f'[私聊] code={private_data.get("code")}')
    except Exception as e:
        private_data = {'code': -1}
        print(f'[私聊] 拉取失败: {e}')

    # 2. 拉取群聊
    payload2 = json.dumps({'studentUid': uid, **time_range}).encode('utf-8')
    try:
        req2 = urllib.request.Request('https://ops.xibojiaoyu.com/xmkp-backend-middle/ops/qwChat/queryGroupChatByStudent', data=payload2, headers=headers)
        res2 = urllib.request.urlopen(req2, timeout=20)
        group_data = json.loads(res2.read().decode('utf-8'))
        print(f'[群聊] code={group_data.get("code")}')
    except Exception as e:
        group_data = {'code': -1}
        print(f'[群聊] 拉取失败: {e}')

    # 3. 保存 JSON
    combined = {'uid': uid, 'private': private_data.get('data', {}), 'group': group_data.get('data', {})}
    json_path = os.path.join(output_dir, f'{uid}.json')
    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(combined, f, ensure_ascii=False, indent=2)

    # 4. 转换 MD
    private = combined.get('private', {})
    group = combined.get('group', {})
    student_name = private.get('studentName', group.get('studentName', '未知'))

    md = [f'# UID {uid} ({student_name}) 聊天记录汇总\n']

    md.append('## 一、私聊记录\n')
    tp = 0
    for tg in private.get('teacherChatGroups', []):
        tn = tg.get('teacherName', '未知')
        mc = tg.get('messageCount', 0)
        tp += mc
        md.append(f'### 与老师: {tn} (共 {mc} 条)\n')
        for m in tg.get('chatMessages', []):
            md.append(f'- **{m.get("senderName","?")}** [{m.get("sendTime","")}]: {m.get("decodeContent","")}')
        md.append('')
    md.append(f'> 私聊总计: {tp} 条\n')

    md.append('## 二、群聊记录\n')
    tg2 = 0
    for gg in group.get('groupChatGroups', []):
        gn = gg.get('groupName', '未知')
        if '#' in gn: gn = gn.split('#')[0]
        mc = gg.get('messageCount', 0)
        tg2 += mc
        md.append(f'### 群组: {gn} (共 {mc} 条)\n')
        for m in gg.get('chatMessages', []):
            md.append(f'- **{m.get("senderName","?")}** [{m.get("sendTime","")}]: {m.get("decodeContent","")}')
        md.append('')
    md.append(f'> 群聊总计: {tg2} 条\n')
    md.append(f'\n---\n> 全部总计: {tp + tg2} 条消息')

    md_path = os.path.join(output_dir, f'{uid}.md')
    with open(md_path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(md))

    def sp(text):
        try: print(text)
        except UnicodeEncodeError: print(text.encode('utf-8', errors='replace').decode('utf-8', errors='replace'))

    sp(f'\n完成! 学员: {student_name}')
    sp(f'   私聊: {tp} 条 | 群聊: {tg2} 条 | 总计: {tp + tg2} 条')
    sp(f'   JSON: {json_path}')
    sp(f'   MD:   {md_path}')

if __name__ == '__main__':
    if len(sys.argv) < 2:
        print('用法: python fetch_chat.py <UID>')
        sys.exit(1)
    uid = int(sys.argv[1])
    fetch_chat(uid)
