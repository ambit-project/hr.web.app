/** Saves content as a file in the browser (files come through server functions, not links to the API). */
export function saveFile(name: string, content: BlobPart, type: string) {
	const url = URL.createObjectURL(new Blob([content], { type }));
	const link = document.createElement("a");
	link.href = url;
	link.download = name;
	link.click();
	URL.revokeObjectURL(url);
}

/** A file a server function returned as base64. */
export function saveBase64File(name: string, base64: string, type: string) {
	const binary = atob(base64);
	const bytes = new Uint8Array(binary.length);
	for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
	saveFile(name, bytes, type);
}
