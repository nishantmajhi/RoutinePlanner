async function addTeacher(teacher) {
  const res = await fetch("/teacher/add", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(teacher),
  });
  if (!res.ok) throw new Error("Failed to add teacher");
  return res.json();
}

async function getTeacher(teacherID) {
  const res = await fetch(`/teacher/get/${teacherID}`);
  if (!res.ok) throw new Error("Failed to get teacher");
  return res.json();
}

async function updateTeacher(teacher) {
  const res = await fetch("/teacher/update", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(teacher),
  });
  if (!res.ok) throw new Error("Failed to update teacher");
  return res.json();
}

async function removeTeacher(teacherID) {
  const res = await fetch(`/teacher/remove/${teacherID}`, {
    method: "DELETE",
  });
  if (!res.ok) throw new Error("Failed to remove teacher");
  return res.json();
}
