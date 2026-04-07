import json, os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

chat_dir = os.path.join('d:', os.sep, 'xibo2026data', '聊天记录')
uids = [318193268, 288154626, 380277605, 551773755, 370048311, 481143108, 676095081, 397226901, 508414321]

for uid in uids:
    fp = os.path.join(chat_dir, str(uid) + '.json')
    if not os.path.exists(fp):
        print(f'{uid}: FILE NOT FOUND')
        continue
    with open(fp, 'r', encoding='utf-8') as f:
        data = json.load(f)
    priv = data.get('private', {})
    grp = data.get('group', {})
    name = priv.get('studentName', grp.get('studentName', '?'))
    tp = sum(t.get('messageCount', 0) for t in priv.get('teacherChatGroups', []))
    tg = sum(g.get('messageCount', 0) for g in grp.get('groupChatGroups', []))

    # Extract first student messages
    first_msgs = []
    for tgrp in priv.get('teacherChatGroups', []):
        for m in tgrp.get('chatMessages', []):
            sn = m.get('senderName', '')
            dc = m.get('decodeContent', '')
            if name and name in sn and dc.strip():
                first_msgs.append(dc[:100])
            if len(first_msgs) >= 5:
                break
        if len(first_msgs) >= 5:
            break

    # Count keywords
    all_text = ''
    for tgrp in priv.get('teacherChatGroups', []):
        for m in tgrp.get('chatMessages', []):
            all_text += m.get('decodeContent', '') + ' '
    for ggrp in grp.get('groupChatGroups', []):
        for m in ggrp.get('chatMessages', []):
            all_text += m.get('decodeContent', '') + ' '

    kw_list = ['退款', '借钱', '没钱', '负债', '投诉', '骗', '便宜', '分期',
               '花呗', '贷款', '被骗', '梨花', '保证', '月入', '赚', '挣',
               '翻身', '老公', '老婆', '孩子', '宝妈', '系统学', '投资',
               '企业', '老板', '资源', '兴趣', '喜欢', '想学', '加油',
               '穷', '负担', '救命', '压力', '焦虑', '不支持', '不知道我报']
    keywords = []
    for kw in kw_list:
        c = all_text.count(kw)
        if c > 0:
            keywords.append(f'{kw}({c})')

    print(f'=== {uid} | {name} | 私聊:{tp} 群聊:{tg} ===')
    kw_str = ' '.join(keywords[:25])
    print(f'  关键词: {kw_str}')
    for i, msg in enumerate(first_msgs[:3]):
        print(f'  学员话[{i+1}]: {msg}')
    print()
