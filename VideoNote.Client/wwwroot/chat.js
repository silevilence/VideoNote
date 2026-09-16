// 聊天滚动辅助：仅当用户本就停留在底部附近时才跟随新内容滚动，回看历史时不打断。
export function pinBottom(el) {
    if (!el) return;
    const distance = el.scrollHeight - el.scrollTop - el.clientHeight;
    if (distance < 200) el.scrollTop = el.scrollHeight;
}
