const active = new Map();
export function upload(input, metadata, receiver) {
    const file = input.files?.[0];
    if (!file) return Promise.reject(new Error("请先选择视频。"));
    if (active.has(input)) return Promise.reject(new Error("正在上传。"));
    return new Promise((resolve, reject) => {
        const body = new FormData();
        body.append("metadata", JSON.stringify({...metadata, fileName: file.name}));
        body.append("video", file);
        const xhr = new XMLHttpRequest();
        active.set(input, xhr);
        xhr.open("POST", "api/tasks/upload");
        let last = 0;
        xhr.upload.onprogress = e => {
            if (e.lengthComputable && (Date.now() - last > 100 || e.loaded === e.total)) {
                last = Date.now();
                receiver.invokeMethodAsync("UploadProgress", Math.round(e.loaded / e.total * 100)).catch(() => {});
            }
        };
        const fail = message => { active.delete(input); reject(new Error(message)); };
        xhr.onerror = () => fail("网络错误，上传未完成。");
        xhr.onabort = () => fail("上传已取消。");
        xhr.onload = () => {
            active.delete(input);
            let body;
            try { body = JSON.parse(xhr.responseText); } catch { reject(new Error("服务端返回无效响应。")); return; }
            if (xhr.status >= 200 && xhr.status < 300) resolve(body);
            else reject(new Error(body.message || Object.values(body.errors || {}).flat().join(" ") || "上传失败（" + xhr.status + "）。"));
        };
        // Native File body: browser streams from disk; video bytes never cross the WASM boundary.
        try { xhr.send(body); }
        catch { fail("无法开始上传，请重试。"); }
    });
}
export function cancel(input) { active.get(input)?.abort(); }
export function selectedFile(input) {
    const file = input.files?.[0];
    return file ? {name: file.name, size: file.size} : null;
}
export function clearFile(input) { input.value = ""; }
export function attachDropzone(zone, input, receiver) {
    if (!zone || zone.dataset.wired) return;
    zone.dataset.wired = "1";
    const notify = () => receiver?.invokeMethodAsync("FilePicked").catch(() => {});
    zone.addEventListener("dragover", e => { e.preventDefault(); zone.classList.add("is-over"); });
    zone.addEventListener("dragleave", () => zone.classList.remove("is-over"));
    zone.addEventListener("drop", e => {
        e.preventDefault();
        zone.classList.remove("is-over");
        if (e.dataTransfer?.files?.length) {
            input.files = e.dataTransfer.files;
            notify();
        }
    });
    input.addEventListener("change", notify);
}
