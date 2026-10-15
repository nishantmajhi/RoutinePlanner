function createElement(tag, attrs = {}, children = []) {
  const node = document.createElement(tag);

  Object.entries(attrs).forEach(([key, value]) => {
    if (key === "style" && typeof value === "object") {
      Object.assign(node.style, value);
    } else if (value === true) {
      node.setAttribute(key, key);
    } else if (value !== false && value != null) {
      node.setAttribute(key, value);
    }
  });

  if (typeof children === "string") {
    node.textContent = children;
  } else if (Array.isArray(children)) {
    children.forEach((child) => node.appendChild(child));
  }

  return node;
}
