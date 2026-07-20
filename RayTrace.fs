#version 330 core
out vec4 FragColor;
in vec2 TexCoords;

uniform float aspectRatio;
uniform float time; 
uniform vec3 camPos;
uniform vec3 camFront;
uniform vec3 camRight;
uniform vec3 camUp;

// ================= 魔法配置区：梦子随时可以修改球的位置和半径 =================
const vec3 BALL_A_POS = vec3(0.0f, 0.0f, -5.0f);   // 1号：樱花玻璃球
const float BALL_A_R  = 1.5f;

const vec3 BALL_B_POS = vec3(2.3f, -0.8f, -4.5f);  // 2号：土豪金金属球
const float BALL_B_R  = 0.9f;

const vec3 BALL_C_POS = vec3(-2.2f, -1.0f, -4.0f); // 3号：翡翠绿塑料球
const float BALL_C_R  = 0.8f;
// =======================================================================

const float IOR_AIR = 1.0f;        
const float IOR_GLASS = 1.52f;     

struct Ray {
    vec3 origin;
    vec3 direction;
};

struct HitRecord {
    float t;
    vec3 p;
    vec3 normal;
    bool frontFace;
    int hitType; 
};

// 伪随机数发生器：用来生成星空和金属粗糙度
float hash(vec3 p) {
    p = fract(p * vec3(443.8975f, 397.2973f, 491.1871f));
    p += dot(p.xyz, p.yzx + 19.19f);
    return fract(p.x * p.y * p.z);
}

// 纯数学生成的多球求交大管家
bool checkSingleSphere(Ray ray, vec3 center, float radius, float tMax, out HitRecord rec) {
    vec3 oc = ray.origin - center;
    float b = dot(oc, ray.direction);
    float c = dot(oc, oc) - radius * radius;
    float h = b * b - c;

    if (h < 0.0f) return false;
    
    float sqrtd = sqrt(h);
    float root = -b - sqrtd;
    if (root < 0.001f || root > tMax) {
        root = -b + sqrtd;
        if (root < 0.001f || root > tMax) return false;
    }

    rec.t = root;
    rec.p = ray.origin + root * ray.direction;
    vec3 outwardNormal = (rec.p - center) / radius;
    rec.frontFace = dot(ray.direction, outwardNormal) < 0.0f;
    rec.normal = rec.frontFace ? outwardNormal : -outwardNormal;
    return true;
}

bool hitAllSpheres(Ray ray, float tMax, out HitRecord rec) {
    HitRecord tempRec;
    bool hitAnything = false;
    float closestSoFar = tMax;

    // --- 1 号球 (樱花玻璃) ---
    if (checkSingleSphere(ray, BALL_A_POS, BALL_A_R, closestSoFar, tempRec)) {
        hitAnything = true;
        closestSoFar = tempRec.t;
        rec = tempRec;
        rec.hitType = 2; // 确认为 2 号玻璃，且 rec.normal 会在内部自动校准为朝外！
    }

    // --- 2 号球 (真·缎面黄金金属) ---
    if (checkSingleSphere(ray, BALL_B_POS, BALL_B_R, closestSoFar, tempRec)) {
        hitAnything = true;
        closestSoFar = tempRec.t;
        rec = tempRec;
        rec.hitType = 3; // 严格锁定 3 号金属材质！
    }

    // --- 3 号球 (翡翠绿塑料) ---
    if (checkSingleSphere(ray, BALL_C_POS, BALL_C_R, closestSoFar, tempRec)) {
        hitAnything = true;
        closestSoFar = tempRec.t;
        rec = tempRec;
        rec.hitType = 4; // 严格锁定 4 号塑料材质！
    }

    return hitAnything;
}

float schlickFresnel(float cosine, float refIdx) {
    float r0 = (1.0f - refIdx) / (1.0f + refIdx);
    r0 = r0 * r0;
    return r0 + (1.0f - r0) * pow(1.0f - cosine, 5.0f);
}

// 核心色彩追踪魔法
vec3 rayColor(Ray initialRay) {
    vec3 finalColor = vec3(0.0f);
    vec3 throughput = vec3(1.0f); 
    Ray currentRay = initialRay;

    for (int bounce = 0; bounce < 4; ++bounce) {
        HitRecord rec;
        rec.t = 1e20f;
        rec.hitType = 0;

        hitAllSpheres(currentRay, rec.t, rec);

        float tPlane = (-2.0f - currentRay.origin.y) / currentRay.direction.y;
        if (currentRay.direction.y < 0.0f && tPlane > 0.001f && tPlane < rec.t) {
            rec.t = tPlane;
            rec.p = currentRay.origin + tPlane * currentRay.direction;
            rec.normal = vec3(0.0f, 1.0f, 0.0f);
            rec.hitType = 1;
        }

        // --- 材质与环境配置阶段 ---

        // 【大魔改】情况 0: 纯数学“璀璨星空与耀眼太阳”环境箱！
        if (rec.hitType == 0) {
            vec3 unitDir = normalize(currentRay.direction);
            
            // 基础大气渐变（极美的科幻深邃感）
            vec3 skyGrad = vec3(0.02f, 0.04f, 0.1f) + (unitDir.y + 1.0f) * vec3(0.08f, 0.12f, 0.25f);
            
            // 魔法 1：凭空捏造一个超强物理太阳 (位于斜上方)
            vec3 sunDir = normalize(vec3(1.0f, 1.5f, -1.0f));
            float sunScore = max(dot(unitDir, sunDir), 0.0f);
            // 太阳耀斑核
            vec3 sunColor = vec3(1.5f, 1.2f, 0.9f) * pow(sunScore, 256.0f) * 15.0f; 
            // 太阳周围的金色光晕
            vec3 sunGlow = vec3(1.0f, 0.6f, 0.3f) * pow(sunScore, 16.0f) * 2.0f;
            
            // 魔法 2：用数学杂色凭空生成的漫天繁星
            float starCheck = hash(floor(unitDir * 120.0f)); 
            vec3 stars = vec3(0.0f);
            if (starCheck > 0.98f && unitDir.y > 0.0f) {
                stars = vec3(pow(hash(unitDir), 10.0f) * 4.0f);
            }

            // 合成最终的纯数学环境光
            finalColor += throughput * (skyGrad + sunColor + sunGlow + stars);
            break; 
        }

        // 情况 1: 地面
        if (rec.hitType == 1) {
            // 1. 基础黑白棋盘格与太阳漫反射
            vec3 sunDir = normalize(vec3(1.0f, 1.5f, -1.0f));
            vec3 floorColor = (sin(rec.p.x * 1.5f) * sin(rec.p.z * 1.5f) > 0.0f) ? vec3(0.7f) : vec3(0.0f);
            float diffuse = max(dot(rec.normal, sunDir), 0.15f);
            
            // 2. 【核心焦散魔法】：凭空在玻璃球下方捏造聚光斑！
            // 计算从 1号玻璃球(BALL_A_POS) 顺着太阳光投射到地面上的“影子中心点”
            // 玻璃球中心 y 是 0.0，地面 y 是 -2.0，高度差是 2.0
            float heightDiff = BALL_A_POS.y - rec.p.y; 
            vec3 shadowCenter = BALL_A_POS - sunDir * (heightDiff / sunDir.y);
            
            // 算出当前地面碰撞点 `rec.p` 距离影子中心的距离
            // 因为玻璃球会把光线像凸透镜一样“向内聚焦”，所以焦点会比影子中心更靠近球体正下方一点点
            vec3 focusCenter = shadowCenter + sunDir * 0.4f; // 向球体下方微调
            float distToFocus = distance(vec3(rec.p.x, 0.0f, rec.p.z), vec3(focusCenter.x, 0.0f, focusCenter.z));
            
            // 用一个非常亮且紧凑的指数函数，画出汇聚的光斑
            // 这里的 0.4f 控制光斑的大小，数值越小光斑越锐利
            float causticIntensity = exp(-distToFocus * distToFocus / (0.15f)); 
            
            // 加上美丽的樱花粉色边缘焦散（因为光线穿过粉色玻璃会染色）
            vec3 causticColor = vec3(1.5f, 0.6f, 0.8f) * causticIntensity * 3.5f; 

            // 3. 把焦散和原本的地面颜色融合
            finalColor += throughput * (floorColor * diffuse + causticColor);
            
            // 地面微弱反射继续前行
            currentRay.origin = rec.p + rec.normal * 0.001f;
            currentRay.direction = reflect(normalize(currentRay.direction), rec.normal);
            throughput *= 0.15f; 
        }

        // 情况 2: 樱花粉玻璃球 (因为有了强大的太阳光，这里会折射出绝美的高光轮廓！)
        if (rec.hitType == 2) {
            float etaiOverEtat = rec.frontFace ? (IOR_AIR / IOR_GLASS) : (IOR_GLASS / IOR_AIR);
            vec3 unitDirection = normalize(currentRay.direction);
            float cosTheta = min(dot(-unitDirection, rec.normal), 1.0f);
            float sinTheta = sqrt(1.0f - cosTheta * cosTheta);

            bool cannotRefract = etaiOverEtat * sinTheta > 1.0f;
            // 算出这一点的精确反射概率
            float reflectProb = schlickFresnel(cosTheta, etaiOverEtat);

            if (rec.frontFace) {
                // 玻璃边缘美丽的积光效果
                finalColor += throughput * vec3(0.5f, 0.7f, 1.0f) * pow(1.0f - cosTheta, 4.0f) * 0.5f;
            }

            vec3 reflectDir = reflect(unitDirection, rec.normal);
            vec3 refractDir = refract(unitDirection, rec.normal, etaiOverEtat);

            if (cannotRefract) {
                currentRay.origin = rec.p + rec.normal * 0.001f;
                currentRay.direction = reflectDir;
            } else {
                // 【无噪点核心】：彻底放弃随机数二选一！
                // 我们直接分流：一部分光能量走反射，剩下的走折射。这就是物理上的能量守恒！
                
                // 1. 顺便计算一下反射光撞击到太阳的高光斑点（给玻璃表面增加一层物理质感）
                vec3 sunDir = normalize(vec3(1.0f, 1.5f, -1.0f));
                float spec = pow(max(dot(reflectDir, sunDir), 0.0f), 32.0f);
                finalColor += throughput * vec3(1.0f) * spec * reflectProb * 2.0f;

                // 2. 让光线继续往玻璃内部折射穿透
                currentRay.origin = rec.p - rec.normal * 0.001f;
                currentRay.direction = refractDir;
                
                // 能量根据折射率衰减，并染上绝美的樱花粉
                throughput *= (1.0f - reflectProb) * vec3(1.0f, 0.45f, 0.65f); 
            }
        }

        // 【完全无噪净化版】情况 3: 丝滑缎面黄金金属球
        if (rec.hitType == 3) {
            // 物理防漏魔法：如果光线是从球体内部撞击内壁(frontFace为false)，直接判定为无效，不着色！
            if (!rec.frontFace) {
                break; 
            }

            vec3 sunDir = normalize(vec3(1.0f, 1.5f, -1.0f));
            vec3 perfectReflect = reflect(normalize(currentRay.direction), rec.normal);
            
            // 真正的表面拉丝缎面效果
            vec3 satinReflect = normalize(mix(perfectReflect, rec.normal, 0.12f)); 
            float surfaceSpec = pow(max(dot(satinReflect, sunDir), 0.2f), 12.0f);
            
            // 把高光直接狠狠地焊在正表面上
            finalColor += throughput * vec3(1.0f, 0.85f, 0.5f) * surfaceSpec * 1.5f; 

            // 光线在【外部】继续反弹
            currentRay.origin = rec.p + rec.normal * 0.001f; 
            currentRay.direction = satinReflect;
            
            // 黄金颜色吸收
            throughput *= vec3(1.0f, 0.85f, 0.45f);
            
        }

        // 情况 4: 翡翠绿塑料球
        if (rec.hitType == 4) {
            vec3 sunDir = normalize(vec3(1.0f, 1.5f, -1.0f));
            float diffuse = max(dot(rec.normal, sunDir), 0.1f);
            vec3 plasticColor = vec3(0.05f, 0.65f, 0.25f) * diffuse;
            
            vec3 viewDir = normalize(-currentRay.direction);
            vec3 halfDir = normalize(sunDir + viewDir);
            float spec = pow(max(dot(rec.normal, halfDir), 0.0f), 32.0f) * 0.6f;
            
            finalColor += throughput * (plasticColor + vec3(spec));
            break; 
        }
    }
    return finalColor;
}

void main() {
    vec2 uv = TexCoords * 2.0f - 1.0f;
    uv.x *= aspectRatio;

    vec3 rayDirection = normalize(camFront * 1.0f + camRight * uv.x + camUp * uv.y);

    Ray primaryRay;
    primaryRay.origin = camPos;
    primaryRay.direction = rayDirection;

    vec3 pixelColor = rayColor(primaryRay);
    
    // 增加一点简单的 HDR 曝光映射和伽马校正，让太阳的高光更柔和漂亮
    pixelColor = pixelColor / (pixelColor + vec3(1.0f));
    FragColor = vec4(pow(pixelColor, vec3(1.0f / 2.2f)), 1.0f);
}