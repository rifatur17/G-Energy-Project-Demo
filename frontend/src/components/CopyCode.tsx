import { useState } from "react";
export function CopyCode({ code }: { code: string }) {
  const [message, setMessage] = useState("");
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(code);
      setMessage("Copied to clipboard");
    } catch {
      setMessage("Copy unavailable. Select and copy the code above.");
    }
  };
  return (
    <>
      <div className="code-box">
        <code>{code}</code>
        <button className="secondary" onClick={copy}>
          Copy ID
        </button>
      </div>
      <span className="hint" role="status">
        {message}
      </span>
    </>
  );
}
